// src/Struo.Infrastructure/Query/FacetQueries.cs
using System.Linq.Expressions;
using SqlSugar;
using Struo.Application.Metadata;
using Struo.Application.Query;
using Struo.Domain.Metadata.Enums;
using Struo.Domain.Metadata.Models;
using Struo.Domain.Query;
using Struo.Infrastructure.Metadata;

namespace Struo.Infrastructure.Query;

internal sealed partial class FacetQueries(
    ISqlSugarClient db, IEntityRegistry registry, IRelationshipGraph graph, IMetadataProvider metadata,
    FilterTranslator filters, TranslationStore translations)
{
    private static readonly GenericDispatcher<Func<FacetQueries, List<IConditionalModel>, DeletedFilter, object>> RootDispatcher =
        new(typeof(FacetQueries), nameof(Root), [typeof(List<IConditionalModel>), typeof(DeletedFilter)]);
    private static readonly GenericDispatcher<Func<FacetQueries, List<IConditionalModel>, object>> RelatedDispatcher =
        new(typeof(FacetQueries), nameof(Related), [typeof(List<IConditionalModel>)]);
    private static readonly BiGenericDispatcher<Func<object, string, KeyValuePair<string, List<SugarParameter>>>> IdsSqlDispatcher =
        new(typeof(FacetQueries), nameof(IdsSql), [typeof(object), typeof(string)]);
    private static readonly BiGenericDispatcher<Func<object, string, string, bool, int, CancellationToken, Task<List<FacetBucket>>>> GroupDispatcher =
        new(typeof(FacetQueries), nameof(Group), [typeof(object), typeof(string), typeof(string), typeof(bool), typeof(int), typeof(CancellationToken)]);

    private object Root<T>(List<IConditionalModel> conds, DeletedFilter deleted) where T : class, new() =>
        DeletedScope.Root<T>(db, conds, deleted);
    private object Related<T>(List<IConditionalModel> conds) where T : class, new() => db.Queryable<T>().Where(conds);
    private static KeyValuePair<string, List<SugarParameter>> IdsSql<T, TId>(object q, string idProperty) where T : class, new() =>
        ((ISugarQueryable<T>)q).Select((Expression<Func<T, TId>>)ColumnSelectorFactory.TypedSelector(typeof(T), idProperty)).ToSql();

    private static async Task<List<FacetBucket>> Group<T, TValue>(object q, string valueProp, string countProp, bool distinct, int take, CancellationToken ct)
        where T : class, new()
    {
        var key = (Expression<Func<T, object>>)ColumnSelectorFactory.BoxedSelector(typeof(T), valueProp);
        var count = (Expression<Func<T, object>>)ColumnSelectorFactory.CountSelector(typeof(T), countProp, distinct);
        var proj = (Expression<Func<T, FacetRow<TValue>>>)ColumnSelectorFactory.FacetProjection(typeof(T), valueProp, countProp, distinct);
        var rows = await ((ISugarQueryable<T>)q).GroupBy(key).OrderBy(count, OrderByType.Desc).OrderBy(key).Select(proj).Take(take).ToListAsync(ct);
        return rows.Select(r => new FacetBucket(r.Value, r.Count)).ToList();
    }

    private RelationshipGraph Graph => graph as RelationshipGraph
        ?? throw new InvalidOperationException("Facets need the concrete RelationshipGraph (junction/reverse-FK descriptors).");

    // The per-relation-facet inputs that ByRelation/ManyToOne/ToMany all need to reach the root's
    // rows — bundled so those methods stay within the parameter-count limit (Sonar S107) without
    // changing behaviour. Not every field is used by every method (e.g. ToMany never queries Root
    // directly), same as FacetRequest at the public seam.
    private readonly record struct RelationFacetContext(
        string Collection, EntityDescriptor Root, object RootQueryable, List<IConditionalModel> Conds, DeletedFilter Deleted, int MaxValues);

    public async Task<IReadOnlyList<FacetBucket>> FacetAsync(FacetRequest request, CancellationToken ct)
    {
        var root = RepositoryHelpers.Descriptor(registry, request.Collection);
        var conds = filters.Translate(
            request.Collection, request.PrunedQuery.Filter, request.PrunedQuery.Search, request.PrunedQuery.SearchCandidates,
            request.SearchableFields, request.QueryLocale);
        var rootQ = RootDispatcher.For(root.EntityType)(this, conds, request.Deleted);
        var facet = request.Facet;

        switch (facet.Kind)
        {
            case FacetPathKind.OwnField:
                return await GroupBuckets(rootQ, root.EntityType, root.FieldToProperty[facet.OwnField!], root.IdProperty, false, request.MaxValues, ct);
            case FacetPathKind.ForeignKey:
                return IdStrings(await GroupBuckets(rootQ, root.EntityType, root.Properties[facet.OwnField!].Name, root.IdProperty, false, request.MaxValues, ct));
            case FacetPathKind.Relation:
                return IdStrings(await ByRelation(new RelationFacetContext(request.Collection, root, rootQ, conds, request.Deleted, request.MaxValues), facet.Relation!, ct));
            case FacetPathKind.RelationLeaf:
                // Feed ByRelation's raw (Guid) id buckets straight into SwapLeafValues — the translation
                // sidecar FK and the target PK are both Guid, so the dictionary keys built in
                // LeafValuesAsync must stay Guid too. Calling IdStrings here first would turn every
                // bucket's Value into a string and none of them would match.
                var ids = await ByRelation(new RelationFacetContext(request.Collection, root, rootQ, conds, request.Deleted, request.MaxValues), facet.Relation!, ct);
                return await SwapLeafValues(ids, facet, request.QueryLocale, request.MaxValues, ct);
            default:
                throw new InvalidOperationException($"Unknown facet kind {facet.Kind}.");
        }
    }

    private Task<List<FacetBucket>> ByRelation(RelationFacetContext ctx, RelationMetadata rel, CancellationToken ct)
    {
        var desc = Graph.Descriptors(ctx.Collection).First(d => string.Equals(d.Meta.Name, rel.Name, StringComparison.OrdinalIgnoreCase));
        var target = RepositoryHelpers.Descriptor(registry, rel.TargetCollection);
        return rel.Kind switch
        {
            RelationKind.ManyToOne => ManyToOne(ctx, ctx.Root.Properties[rel.ForeignKey!].Name, ct),
            RelationKind.OneToMany => ToMany(ctx, target.EntityType, desc.ReverseForeignKeyProperty!, target.IdProperty, desc.ReverseForeignKeyProperty!, ct),
            RelationKind.ManyToMany => ToMany(ctx, desc.JunctionType!, desc.JunctionParentFk!, desc.JunctionTargetFk!, desc.JunctionParentFk!, ct),
            _ => throw new QueryException($"Unsupported relation kind '{rel.Kind}'."),
        };
    }

    private Task<List<FacetBucket>> ManyToOne(RelationFacetContext ctx, string fkProperty, CancellationToken ct)
    {
        var notNull = new ConditionalModel
        {
            FieldName = db.EntityMaintenance.GetDbColumnName(fkProperty, ctx.Root.EntityType), ConditionalType = ConditionalType.IsNot, FieldValue = null,
        };
        var q = RootDispatcher.For(ctx.Root.EntityType)(this, [.. ctx.Conds, notNull], ctx.Deleted);
        return GroupBuckets(q, ctx.Root.EntityType, fkProperty, ctx.Root.IdProperty, false, ctx.MaxValues, ct);
    }

    private Task<List<FacetBucket>> ToMany(RelationFacetContext ctx, Type sideType, string rootRefProperty, string valueProperty, string countProperty, CancellationToken ct)
    {
        var idType = ColumnSelectorFactory.PropertyType(ctx.Root.EntityType, ctx.Root.IdProperty);
        var idsSql = IdsSqlDispatcher.For(ctx.Root.EntityType, idType)(ctx.RootQueryable, ctx.Root.IdProperty);
        // Root ids feed the related/junction side as one subquery, composed the same way relation-filter
        // pushdown composes nested subqueries (FilterTranslator.Subquery.cs): through
        // SubQueryConditional.Wrap over a plain ToSql() KeyValuePair, never SqlSugar's
        // In(Expression, ISugarQueryable) overload — that overload cannot rename the inner query's
        // parameters and so collides whenever two subqueries sit at the same level.
        var wrapped = SubQueryConditional.Wrap(SubQueryKind.In, db.EntityMaintenance.GetDbColumnName(rootRefProperty, sideType), idsSql);
        var q = RelatedDispatcher.For(sideType)(this, [wrapped]);
        return GroupBuckets(q, sideType, valueProperty, countProperty, true, ctx.MaxValues, ct);
    }

    // Non-generic dispatch wrapper around the generic Group<T,TValue> static method above. Named
    // distinctly (Sonar S4136: all overloads of one name must be adjacent) since GroupDispatcher
    // resolves Group<T,TValue> by reflection via nameof(Group) and a same-named non-generic overload
    // here would give that lookup two candidates.
    private static Task<List<FacetBucket>> GroupBuckets(object q, Type entityType, string valueProp, string countProp, bool distinct, int take, CancellationToken ct) =>
        GroupDispatcher.For(entityType, ColumnSelectorFactory.PropertyType(entityType, valueProp))(q, valueProp, countProp, distinct, take, ct);

    private static List<FacetBucket> IdStrings(List<FacetBucket> buckets) =>
        buckets.Select(b => new FacetBucket(b.Value?.ToString(), b.Count)).ToList();
}
