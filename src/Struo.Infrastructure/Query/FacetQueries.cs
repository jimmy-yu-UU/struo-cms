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

    public async Task<IReadOnlyList<FacetBucket>> FacetAsync(
        string collection, QueryModel pruned, ResolvedFacetPath facet, IReadOnlyList<string> searchableFields,
        string? queryLocale, DeletedFilter deleted, int maxValues, CancellationToken ct)
    {
        var root = RepositoryHelpers.Descriptor(registry, collection);
        var conds = filters.Translate(collection, pruned.Filter, pruned.Search, searchableFields, queryLocale);
        var rootQ = RootDispatcher.For(root.EntityType)(this, conds, deleted);

        switch (facet.Kind)
        {
            case FacetPathKind.OwnField:
                return await Group(rootQ, root.EntityType, root.FieldToProperty[facet.OwnField!], root.IdProperty, false, maxValues, ct);
            case FacetPathKind.ForeignKey:
                return IdStrings(await Group(rootQ, root.EntityType, root.Properties[facet.OwnField!].Name, root.IdProperty, false, maxValues, ct));
            case FacetPathKind.Relation:
                return IdStrings(await ByRelation(collection, root, rootQ, conds, deleted, facet.Relation!, maxValues, ct));
            case FacetPathKind.RelationLeaf:
                // Feed ByRelation's raw (Guid) id buckets straight into SwapLeafValues — the translation
                // sidecar FK and the target PK are both Guid, so the dictionary keys built in
                // LeafValuesAsync must stay Guid too. Calling IdStrings here first would turn every
                // bucket's Value into a string and none of them would match.
                var ids = await ByRelation(collection, root, rootQ, conds, deleted, facet.Relation!, maxValues, ct);
                return await SwapLeafValues(ids, facet, queryLocale, maxValues, ct);
            default:
                throw new InvalidOperationException($"Unknown facet kind {facet.Kind}.");
        }
    }

    private Task<List<FacetBucket>> ByRelation(string collection, EntityDescriptor root, object rootQ, List<IConditionalModel> conds, DeletedFilter deleted,
        RelationMetadata rel, int maxValues, CancellationToken ct)
    {
        var desc = Graph.Descriptors(collection).First(d => string.Equals(d.Meta.Name, rel.Name, StringComparison.OrdinalIgnoreCase));
        var target = RepositoryHelpers.Descriptor(registry, rel.TargetCollection);
        return rel.Kind switch
        {
            RelationKind.ManyToOne => ManyToOne(root, conds, deleted, root.Properties[rel.ForeignKey!].Name, maxValues, ct),
            RelationKind.OneToMany => ToMany(target.EntityType, root, rootQ, desc.ReverseForeignKeyProperty!, target.IdProperty, desc.ReverseForeignKeyProperty!, maxValues, ct),
            RelationKind.ManyToMany => ToMany(desc.JunctionType!, root, rootQ, desc.JunctionParentFk!, desc.JunctionTargetFk!, desc.JunctionParentFk!, maxValues, ct),
            _ => throw new QueryException($"Unsupported relation kind '{rel.Kind}'."),
        };
    }

    private Task<List<FacetBucket>> ManyToOne(EntityDescriptor root, List<IConditionalModel> conds, DeletedFilter deleted, string fkProperty, int maxValues, CancellationToken ct)
    {
        var notNull = new ConditionalModel
        {
            FieldName = db.EntityMaintenance.GetDbColumnName(fkProperty, root.EntityType), ConditionalType = ConditionalType.IsNot, FieldValue = null,
        };
        var q = RootDispatcher.For(root.EntityType)(this, [.. conds, notNull], deleted);
        return Group(q, root.EntityType, fkProperty, root.IdProperty, false, maxValues, ct);
    }

    private Task<List<FacetBucket>> ToMany(Type sideType, EntityDescriptor root, object rootQ, string rootRefProperty, string valueProperty, string countProperty, int maxValues, CancellationToken ct)
    {
        var idType = ColumnSelectorFactory.PropertyType(root.EntityType, root.IdProperty);
        var idsSql = IdsSqlDispatcher.For(root.EntityType, idType)(rootQ, root.IdProperty);
        // Root ids feed the related/junction side as one subquery, composed the same way relation-filter
        // pushdown composes nested subqueries (FilterTranslator.Subquery.cs): through
        // SubQueryConditional.Wrap over a plain ToSql() KeyValuePair, never SqlSugar's
        // In(Expression, ISugarQueryable) overload — that overload cannot rename the inner query's
        // parameters and so collides whenever two subqueries sit at the same level.
        var wrapped = SubQueryConditional.Wrap(SubQueryKind.In, db.EntityMaintenance.GetDbColumnName(rootRefProperty, sideType), idsSql);
        var q = RelatedDispatcher.For(sideType)(this, [wrapped]);
        return Group(q, sideType, valueProperty, countProperty, true, maxValues, ct);
    }

    private static Task<List<FacetBucket>> Group(object q, Type entityType, string valueProp, string countProp, bool distinct, int take, CancellationToken ct) =>
        GroupDispatcher.For(entityType, ColumnSelectorFactory.PropertyType(entityType, valueProp))(q, valueProp, countProp, distinct, take, ct);

    private static List<FacetBucket> IdStrings(List<FacetBucket> buckets) =>
        buckets.Select(b => new FacetBucket(b.Value?.ToString(), b.Count)).ToList();
}
