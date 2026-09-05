// src/Struo.Infrastructure/Query/FilterTranslator.cs
using SqlSugar;
using Struo.Application.Configuration;
using Struo.Application.Metadata;
using Struo.Application.Query;
using Struo.Domain.Query;

namespace Struo.Infrastructure.Query;

/// <summary>Turns a validated FilterNode tree (plus a search term) into SqlSugar conditionals for ONE
/// queryable over <c>collection</c>. Own-collection leaves go through ConditionalModelTranslator; relation
/// paths, relation predicates and translatable leaves become IN (SELECT …) subquery conditionals built in
/// FilterTranslator.Subquery.cs. Pure construction — never touches the database.</summary>
internal sealed partial class FilterTranslator(
    ISqlSugarClient db, IRelationshipGraph graph, IMetadataProvider metadata, IEntityRegistry registry, StruoQueryOptions options)
{
    public List<IConditionalModel> Translate(
        string collection, FilterNode? filter, string? search, IReadOnlyList<string> searchableFields, string? queryLocale)
    {
        var models = new List<IConditionalModel>();
        if (filter is not null) AppendModel(models, collection, filter, queryLocale);
        if (!string.IsNullOrWhiteSpace(search) && searchableFields.Count > 0)
            models.Add(SearchGroup(collection, search, searchableFields, queryLocale));
        return models;
    }

    // Appends node's translation into a PLAIN list (SqlSugar ANDs consecutive top-level
    // IConditionalModel entries with no keyword synthesis, verified against SqlSugarCore 5.1.4.217).
    // An AND node flattens its children straight into that list instead of going through
    // ConditionalCollections — measured empirically: SqlSugar's ConditionalCollections rendering
    // double-inserts an "AND" keyword whenever TWO ADJACENT entries in one ConditionalList are both
    // ICustomConditionalFunc-backed (which every relation/predicate/translatable conditional here
    // is, via SubQueryConditional.Wrap), regardless of the group's own WhereType (AND or OR) — e.g.
    // "... IN (...) AND  AND CategoryId IN (...) ..." or "... IN (...) OR  AND CategoryId IN (...)
    // ...", both syntax errors. A single custom entry, or a custom entry adjacent only to plain
    // (non-custom) entries, renders correctly. Flattening AND into the plain list sidesteps
    // ConditionalCollections (and the bug) entirely for the AND case, which is every case this
    // translator needs — OR still uses ConditionalCollections (LeafModel), which is only ever
    // exercised here with at most one custom child per group.
    private void AppendModel(List<IConditionalModel> models, string collection, FilterNode node, string? queryLocale)
    {
        if (node is LogicalFilter { Op: LogicalOperator.And } l)
        {
            foreach (var child in l.Children) AppendModel(models, collection, child, queryLocale);
            return;
        }
        models.Add(ToModel(collection, node, queryLocale));
    }

    private IConditionalModel ToModel(string collection, FilterNode node, string? queryLocale) => node switch
    {
        ComparisonFilter c => Leaf(collection, c, queryLocale),
        RelationPredicateFilter p => Predicate(collection, p, queryLocale),
        LogicalFilter { Op: LogicalOperator.Or } l => new ConditionalCollections
        {
            ConditionalList = l.Children
                .Select(ch => new KeyValuePair<WhereType, ConditionalModel>(WhereType.Or, LeafModel(collection, ch, queryLocale)))
                .ToList()
        },
        // An AND node only reaches here nested inside an OR group's child (LeafModel), which SqlSugar's
        // ConditionalCollections cannot express (its ConditionalList holds ConditionalModel values, and
        // ConditionalCollections is not itself a ConditionalModel — an AND sub-group has no single-value
        // form to hand it). AppendModel (used everywhere an AND can flatten into a plain list) covers
        // every case this translator's own tests exercise; this is the one shape it cannot reach.
        LogicalFilter { Op: LogicalOperator.And } =>
            throw new QueryException("An '_and' group nested inside an '_or' group is not supported."),
        _ => throw new InvalidOperationException($"Unknown filter node type: {node.GetType().Name}")
    };

    // Children of a logical group must be ConditionalModel (ConditionalCollections cannot nest). Own leaves
    // AND subquery-wrapped relation nodes are both ConditionalModel, so either can sit inside an OR group.
    private ConditionalModel LeafModel(string collection, FilterNode node, string? queryLocale) => node switch
    {
        ComparisonFilter c => Leaf(collection, c, queryLocale),
        RelationPredicateFilter p => Predicate(collection, p, queryLocale),
        _ => throw new InvalidOperationException(
            $"Nested logical filter reached the translator for node type '{node.GetType().Name}'; QueryValidator should have rejected it.")
    };

    private ConditionalModel Leaf(string collection, ComparisonFilter c, string? queryLocale)
    {
        if (RelationPath.IsRelationPath(c.FieldPath)) return DottedPath(collection, c, queryLocale);
        if (queryLocale is not null && IsTranslatable(collection, c.FieldPath)) return TranslatableLeaf(collection, c, queryLocale);
        return ConditionalModelTranslator.ToSingleModel(c, RepositoryHelpers.Descriptor(registry, collection), db);
    }

    // One OR group: LIKE per non-translatable searchable field, one translation-sidecar subquery per
    // translatable searchable field (same group, so "non-translatable OR translatable" stays one statement).
    private ConditionalCollections SearchGroup(string collection, string search, IReadOnlyList<string> searchableFields, string? queryLocale)
    {
        var list = searchableFields
            .Select(f => new KeyValuePair<WhereType, ConditionalModel>(WhereType.Or,
                Leaf(collection, new ComparisonFilter(f, QueryOperator.Contains, search), queryLocale)))
            .ToList();
        return new ConditionalCollections { ConditionalList = list };
    }

    private bool IsTranslatable(string collection, string field)
    {
        var tm = metadata.GetCollection(collection)?.Translation;
        return tm is not null && tm.Fields.Any(f => string.Equals(f, field, StringComparison.OrdinalIgnoreCase));
    }
}
