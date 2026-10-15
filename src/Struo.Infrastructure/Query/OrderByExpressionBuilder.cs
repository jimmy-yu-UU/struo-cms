// src/Struo.Infrastructure/Query/OrderByExpressionBuilder.cs
using System.Linq.Expressions;
using System.Reflection;
using SqlSugar;
using Struo.Application.Configuration;
using Struo.Application.Metadata;
using Struo.Application.Query;
using Struo.Domain.Query;

namespace Struo.Infrastructure.Query;

/// <summary>One ORDER BY key: a typed sort-key lambda (<c>entity =&gt; object</c>) and its direction.</summary>
public sealed record OrderTerm(LambdaExpression Selector, OrderByType Direction);

/// <summary>
/// Builds typed ORDER BY terms for item queries: plain columns, locale-scoped translatable fields
/// (correlated subquery, locale bound as a parameter) and to-one relation paths (nested subqueries).
/// Also injects a stable default order and a PK tiebreak: without them PostgreSQL's heap
/// order is in effect, so an UPDATE (which rewrites the tuple elsewhere in the heap) makes the
/// edited row visibly jump position in the admin list.
/// </summary>
public sealed class OrderByExpressionBuilder(
    IEntityRegistry registry,
    IRelationshipGraph graph,
    IMetadataProvider metadata,
    StruoQueryOptions options)
{
    public IReadOnlyList<OrderTerm> BuildOrderBy(IReadOnlyList<SortField> sort, EntityDescriptor d, string collection, string? queryLocale = null)
    {
        // No client sort: PostgreSQL heap order is unstable across UPDATEs (MVCC rewrites the tuple
        // elsewhere), so an edited row visibly jumps in the list. Default to newest-first with the PK
        // ascending, in the database's own Guid order, as tiebreak
        // (docs/ai/decisions/guid-ordering-per-backend.md); entities without CreatedAt fall back to PK alone.
        if (sort.Count == 0) return DefaultOrder(d);

        var collMeta = metadata.GetCollection(collection);
        var terms = sort.Select(s => new OrderTerm(SortKey(s, d, collection, collMeta, queryLocale), Direction(s.Descending))).ToList();

        // Stable pagination: a client sort on a non-unique column leaves tied rows in undefined
        // order, so Skip/Take can repeat or drop rows across pages. Append the PK unless the
        // caller already sorts by it.
        if (!sort.Any(s => string.Equals(s.Field, "id", StringComparison.OrdinalIgnoreCase)))
            terms.Add(Column(d, d.IdProperty, OrderByType.Asc));
        return terms;
    }

    private static OrderByType Direction(bool descending) => descending ? OrderByType.Desc : OrderByType.Asc;

    private static OrderTerm Column(EntityDescriptor d, string property, OrderByType direction) =>
        new(ColumnSelectorFactory.BoxedSelector(d.EntityType, property), direction);

    private static IReadOnlyList<OrderTerm> DefaultOrder(EntityDescriptor d) =>
        d.Properties.ContainsKey("CreatedAt")
            ? [Column(d, "CreatedAt", OrderByType.Desc), Column(d, d.IdProperty, OrderByType.Asc)]
            : [Column(d, d.IdProperty, OrderByType.Asc)];

    private LambdaExpression SortKey(
        SortField s, EntityDescriptor d, string collection,
        Domain.Metadata.Models.CollectionMetadata? collMeta, string? queryLocale)
    {
        if (RelationPath.IsRelationPath(s.Field))
            return RelationKey(collection, s.Field);

        var translation = collMeta?.Translation;
        if (queryLocale is not null && translation is not null
            && translation.Fields.Any(f => string.Equals(f, s.Field, StringComparison.OrdinalIgnoreCase)))
        {
            return TranslatableKey(s.Field, translation, queryLocale, d);
        }

        var prop = d.FieldToProperty.TryGetValue(s.Field, out var p) ? p : s.Field;
        return ColumnSelectorFactory.BoxedSelector(d.EntityType, prop);
    }

    private static LambdaExpression TranslatableKey(
        string fieldName, Domain.Metadata.Models.TranslationMetadata tm, string queryLocale, EntityDescriptor parentDesc)
    {
        var fieldProperty = tm.TranslationEntityType
            .GetProperty(fieldName, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase)?.Name ?? fieldName;
        return SortExpressionFactory.TranslatableSubquery(
            parentDesc.EntityType, parentDesc.IdProperty, tm.TranslationEntityType,
            tm.ForeignKeyProperty, tm.LocaleProperty, fieldProperty, queryLocale);
    }

    /// <summary>
    /// A to-one relation path (validated all-to-one by QueryValidator before reaching here), e.g.
    /// <c>category.parent.name</c> sorts by the parent category's name of the article's category.
    /// </summary>
    private LambdaExpression RelationKey(string rootCollection, string path)
    {
        var rp = RelationPath.Parse(rootCollection, path, graph, metadata, options.MaxRelationDepth);
        var segs = rp.Segments;

        string FkProperty(EntityDescriptor d, string fk) =>
            d.FieldToProperty.TryGetValue(fk, out var p) ? p
                : throw new QueryException($"Foreign key '{fk}' is not a known property on '{d.EntityType.Name}'.");

        var rootDesc = registry.Get(rootCollection)!;
        var hops = new List<SortHop>();
        var previous = rootDesc;
        foreach (var seg in segs)
        {
            var target = registry.Get(seg.Relation.TargetCollection)!;
            hops.Add(new SortHop(target.EntityType, target.IdProperty, FkProperty(previous, seg.Relation.ForeignKey!)));
            previous = target;
        }

        var leafProperty = previous.FieldToProperty.TryGetValue(rp.LeafField, out var lp) ? lp : rp.LeafField;
        return SortExpressionFactory.RelationPath(rootDesc.EntityType, hops, leafProperty);
    }
}
