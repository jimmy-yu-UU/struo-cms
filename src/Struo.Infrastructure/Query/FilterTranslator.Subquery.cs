// src/Struo.Infrastructure/Query/FilterTranslator.Subquery.cs
using System.Linq.Expressions;
using System.Reflection;
using SqlSugar;
using Struo.Application.Metadata;
using Struo.Application.Query;
using Struo.Domain.Metadata.Enums;
using Struo.Domain.Query;
using Struo.Infrastructure.Metadata;

namespace Struo.Infrastructure.Query;

internal sealed partial class FilterTranslator
{
    // A built subquery: the boxed ISugarQueryable<FieldType> the level ABOVE compares against — via
    // `CompareProperty`, a CLR property on `CompareOn` (the declaring-side entity type). Nesting composes
    // by wrapping one level's ToSql() output as a ConditionalModel inside the next level's own Where list
    // (see Over/Wrap below) — never by SqlSugar's In(Expression, ISugarQueryable) overload, which cannot
    // rename the inner query's parameters and so collides across nesting levels (see SubQueryConditional).
    private readonly record struct SubqueryRef(object Queryable, Type FieldType, Type CompareOn, string CompareProperty);

    private static readonly GenericDispatcher<Func<FilterTranslator, List<IConditionalModel>, object>> NewQueryableDispatcher =
        new(typeof(FilterTranslator), nameof(NewQueryable), [typeof(List<IConditionalModel>)]);
    private static readonly BiGenericDispatcher<Func<FilterTranslator, object, LambdaExpression, object>> ProjectDispatcher =
        new(typeof(FilterTranslator), nameof(Project), [typeof(object), typeof(LambdaExpression)]);
    private static readonly GenericDispatcher<Func<FilterTranslator, object, string, object>> GuardDispatcher =
        new(typeof(FilterTranslator), nameof(Guard), [typeof(object), typeof(string)]);
    private static readonly GenericDispatcher<Func<FilterTranslator, object, KeyValuePair<string, List<SugarParameter>>>> ToSqlDispatcher =
        new(typeof(FilterTranslator), nameof(ToSql), [typeof(object)]);

    private object NewQueryable<T>(List<IConditionalModel> conds) where T : class, new() => db.Queryable<T>().Where(conds);
    private object Project<T, TField>(object q, LambdaExpression projection) where T : class, new() =>
        ((ISugarQueryable<T>)q).Select((Expression<Func<T, TField>>)projection);
    private object Guard<T>(object q, string column) where T : class, new() =>
        ((ISugarQueryable<T>)q).Where(new List<IConditionalModel> { new ConditionalModel { FieldName = column, ConditionalType = ConditionalType.IsNot, FieldValue = null } });
    private KeyValuePair<string, List<SugarParameter>> ToSql<TField>(object q) => ((ISugarQueryable<TField>)q).ToSql();

    private RelationshipGraph Graph => graph as RelationshipGraph
        ?? throw new InvalidOperationException("Relation filters need the concrete RelationshipGraph (junction/reverse-FK descriptors).");

    // ── entry points ──────────────────────────────────────────────────────

    // Dotted path: each ComparisonFilter is its own subquery chain (each-exists semantics).
    private ConditionalModel DottedPath(string collection, ComparisonFilter c, string? queryLocale)
    {
        var path = RelationPath.Parse(collection, c.FieldPath, graph, metadata, options.MaxRelationDepth);
        var leaf = new ComparisonFilter(path.LeafField, c.Op, c.Value);
        var sub = Chain(path.Segments, 0, path.IsJunctionLeaf ? null : leaf, path.IsJunctionLeaf ? leaf : null, queryLocale, forNone: false);
        return Wrap(SubQueryKind.In, sub);
    }

    private ConditionalModel Predicate(string collection, RelationPredicateFilter p, string? queryLocale)
    {
        var path = RelationPath.ParseRelationOnly(collection, p.RelationPath, graph, metadata, options.MaxRelationDepth);
        var (targetInner, junctionInner) = SplitJunctionConditions(p.Inner);
        var forNone = p.Quantifier == RelationQuantifier.None;
        var sub = Chain(path.Segments, 0, targetInner, junctionInner, queryLocale, forNone);
        if (!forNone) return Wrap(SubQueryKind.In, sub);
        var firstIsM2O = path.Segments[0].Relation.Kind == RelationKind.ManyToOne;
        return Wrap(firstIsM2O ? SubQueryKind.NullOrNotIn : SubQueryKind.NotIn, sub);
    }

    // Translatable own-collection leaf: <coll>.id IN (SELECT fk FROM <translation> WHERE locale = ? AND field op ?)
    private ConditionalModel TranslatableLeaf(string collection, ComparisonFilter c, string queryLocale) =>
        Wrap(SubQueryKind.In, TranslationSubquery(collection, c, queryLocale));

    // ── chain construction ───────────────────────────────────────────────

    // Subquery for segs[i..]: a queryable over the segment's TARGET side (junction table for M2M) projecting
    // the value the DECLARING side compares against. `terminal`/`junctionLeaf` apply at the last segment.
    private SubqueryRef Chain(IReadOnlyList<RelationSegment> segs, int i, FilterNode? terminal, FilterNode? junctionLeaf,
        string? queryLocale, bool forNone)
    {
        var seg = segs[i];
        var desc = Graph.Descriptors(seg.DeclaringCollection)
            .First(d => string.Equals(d.Meta.Name, seg.RelationName, StringComparison.OrdinalIgnoreCase));
        var declaring = RepositoryHelpers.Descriptor(registry, seg.DeclaringCollection);
        var target = RepositoryHelpers.Descriptor(registry, seg.Relation.TargetCollection);
        var isLast = i == segs.Count - 1;

        List<IConditionalModel> targetConds = [];
        SubqueryRef? next = null;
        if (isLast) { if (terminal is not null) AppendModel(targetConds, seg.Relation.TargetCollection, terminal, queryLocale); }
        else next = Chain(segs, i + 1, terminal, junctionLeaf, queryLocale, forNone);

        return seg.Relation.Kind switch
        {
            RelationKind.ManyToOne => Projected(
                Over(target.EntityType, targetConds, next), target.EntityType, target.IdProperty,
                declaring.EntityType, declaring.FieldToProperty[seg.Relation.ForeignKey!], guard: forNone),
            RelationKind.OneToMany => Projected(
                Over(target.EntityType, targetConds, next), target.EntityType, desc.ReverseForeignKeyProperty!,
                declaring.EntityType, declaring.IdProperty, guard: true),          // reverse FK is nullable: always guard
            RelationKind.ManyToMany => ManyToManyHop(seg, desc, declaring, target, targetConds, next, isLast ? junctionLeaf : null, queryLocale, forNone),
            _ => throw new QueryException($"Unsupported relation kind '{seg.Relation.Kind}'."),
        };
    }

    // declaring.id IN (SELECT parentFk FROM junction WHERE [junction conds] [AND targetFk IN (SELECT id FROM target WHERE …)])
    private SubqueryRef ManyToManyHop(RelationSegment seg, RelationDescriptor desc, EntityDescriptor declaring, EntityDescriptor target,
        List<IConditionalModel> targetConds, SubqueryRef? next, FilterNode? junctionLeaf, string? queryLocale, bool forNone)
    {
        var junctionConds = new List<IConditionalModel>();
        if (junctionLeaf is not null)
        {
            var junctionCollection = desc.JunctionCollection
                ?? throw new QueryException($"Relation '{seg.RelationName}' on '{seg.DeclaringCollection}' has no junction collection; '_junction' is not available.");
            AppendModel(junctionConds, junctionCollection, junctionLeaf, queryLocale);
        }
        SubqueryRef? targetSub = null;
        if (targetConds.Count > 0 || next is not null)
            targetSub = Projected(Over(target.EntityType, targetConds, next), target.EntityType, target.IdProperty,
                desc.JunctionType!, desc.JunctionTargetFk!, guard: false);
        return Projected(Over(desc.JunctionType!, junctionConds, targetSub), desc.JunctionType!, desc.JunctionParentFk!,
            declaring.EntityType, declaring.IdProperty, guard: forNone);
    }

    // db.Queryable<T>().Where(conds)[.Where(<compare column> IN (<next's ToSql()>))] — nesting composes
    // through SubQueryConditional.Wrap (renamed, parameterized-safe), never through SqlSugar's
    // In(Expression, ISugarQueryable) overload (see the SubqueryRef doc comment above).
    private object Over(Type entityType, List<IConditionalModel> conds, SubqueryRef? next)
    {
        if (next is not { } n) return NewQueryableDispatcher.For(entityType)(this, conds);
        var column = db.EntityMaintenance.GetDbColumnName(n.CompareProperty, entityType);
        var sql = ToSqlDispatcher.For(n.FieldType)(this, n.Queryable);
        var wrapped = SubQueryConditional.Wrap(SubQueryKind.In, column, sql);
        return NewQueryableDispatcher.For(entityType)(this, [.. conds, wrapped]);
    }

    // [.Where(<projectProperty> IS NOT NULL)].Select(x => x.<projectProperty>) — compared by the level above
    // against <compareProperty> on <compareOn>.
    private SubqueryRef Projected(object q, Type entityType, string projectProperty, Type compareOn, string compareProperty, bool guard)
    {
        if (guard) q = GuardDispatcher.For(entityType)(this, q, db.EntityMaintenance.GetDbColumnName(projectProperty, entityType));
        var fieldType = ColumnSelectorFactory.PropertyType(entityType, projectProperty);
        var projected = ProjectDispatcher.For(entityType, fieldType)(this, q, ColumnSelectorFactory.TypedSelector(entityType, projectProperty));
        return new SubqueryRef(projected, fieldType, compareOn, compareProperty);
    }

    private ConditionalModel Wrap(SubQueryKind kind, SubqueryRef sub)
    {
        var column = db.EntityMaintenance.GetDbColumnName(sub.CompareProperty, sub.CompareOn);
        var sql = ToSqlDispatcher.For(sub.FieldType)(this, sub.Queryable);
        return SubQueryConditional.Wrap(kind, column, sql);
    }

    // Splits a predicate's inner filter into target-side and `_junction.`-prefixed conditions. RelationPath/
    // QueryValidator already made sure `_junction` only appears after a payload M2M relation.
    private static (FilterNode? Target, FilterNode? Junction) SplitJunctionConditions(FilterNode inner)
    {
        var prefix = FilterReservedTokens.Junction + ".";
        if (inner is LogicalFilter { Op: LogicalOperator.Or } orGroup &&
            orGroup.Children.OfType<ComparisonFilter>().Any(c => c.FieldPath.StartsWith(prefix, StringComparison.Ordinal)))
            throw new QueryException("'_junction' conditions cannot be OR-ed with target-collection conditions inside one predicate.");
        var children = inner is LogicalFilter { Op: LogicalOperator.And } l ? l.Children : [inner];
        var target = new List<FilterNode>();
        var junction = new List<FilterNode>();
        foreach (var ch in children)
        {
            if (ch is ComparisonFilter c && c.FieldPath.StartsWith(prefix, StringComparison.Ordinal))
                junction.Add(c with { FieldPath = c.FieldPath[prefix.Length..] });
            else target.Add(ch);
        }
        return (Fold(target), Fold(junction));
    }

    private static FilterNode? Fold(List<FilterNode> nodes) =>
        nodes.Count switch { 0 => null, 1 => nodes[0], _ => new LogicalFilter(LogicalOperator.And, nodes) };

    // Translation sidecar: <coll>.id IN (SELECT <fk> FROM <translation> WHERE <locale> = ? AND <field> op ?)
    private SubqueryRef TranslationSubquery(string collection, ComparisonFilter c, string queryLocale)
    {
        var own = RepositoryHelpers.Descriptor(registry, collection);
        var tm = metadata.GetCollection(collection)!.Translation!;
        var tType = tm.TranslationEntityType;
        var conds = new List<IConditionalModel>
        {
            new ConditionalModel { FieldName = db.EntityMaintenance.GetDbColumnName(tm.LocaleProperty, tType), ConditionalType = ConditionalType.Equal, FieldValue = queryLocale },
            ConditionalModelTranslator.ToSingleModel(c, TranslationDescriptor(tType), db),
        };
        return Projected(NewQueryableDispatcher.For(tType)(this, conds), tType, tm.ForeignKeyProperty, own.EntityType, own.IdProperty, guard: false);
    }

    // camelCase -> CLR property map for a translation entity (moved verbatim from TranslationStore.BuildTranslationDescriptor).
    private static EntityDescriptor TranslationDescriptor(Type translationType)
    {
        var props = translationType.GetProperties(BindingFlags.Public | BindingFlags.Instance);
        var map = props.ToDictionary(p => char.ToLowerInvariant(p.Name[0]) + p.Name[1..], p => p.Name, StringComparer.OrdinalIgnoreCase);
        var idProp = props.FirstOrDefault(p => p.GetCustomAttribute<SugarColumn>() is { IsPrimaryKey: true })?.Name ?? "Id";
        return new EntityDescriptor(translationType, map, idProp);
    }
}
