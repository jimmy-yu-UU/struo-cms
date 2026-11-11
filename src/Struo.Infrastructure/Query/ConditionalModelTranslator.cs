// src/Struo.Infrastructure/Query/ConditionalModelTranslator.cs
using System.Reflection;
using SqlSugar;
using Struo.Application.Metadata;
using Struo.Domain.Query;
using Struo.Infrastructure.Persistence;

namespace Struo.Infrastructure.Query;

public static class ConditionalModelTranslator
{
    /// <summary>
    /// Translates a <see cref="FilterNode"/> AST and/or a search term into a list of
    /// SqlSugar <see cref="IConditionalModel"/> objects suitable for use with
    /// <c>Queryable&lt;T&gt;().Where(list)</c>.
    /// </summary>
    public static List<IConditionalModel> Translate(
        FilterNode? filter,
        string? search,
        IReadOnlyList<string> searchableFields,
        EntityDescriptor descriptor,
        ISqlSugarClient db)
    {
        var models = new List<IConditionalModel>();

        if (filter is not null && ToModel(filter, descriptor, db) is { } model)
            models.Add(model);

        if (!string.IsNullOrWhiteSpace(search) && searchableFields.Count > 0)
        {
            // The FIRST entry's WhereType is the connector between this group and whatever
            // precedes it in the enclosing plain list (or is harmlessly dropped when there is
            // no predecessor); every OTHER entry joins the previous entry INSIDE the group's own
            // parentheses (mirrors FilterTranslator.BuildOrCollection). A first entry of
            // WhereType.Or renders "<precedingFilter> OR (a OR b)" — the connector defect fix
            // round 3 removed from FilterTranslator; the first entry here must always be And.
            var or = new ConditionalCollections
            {
                ConditionalList = searchableFields
                    .Select((f, i) => new KeyValuePair<WhereType, ConditionalModel>(
                        i == 0 ? WhereType.And : WhereType.Or,
                        new ConditionalModel
                        {
                            FieldName = Column(descriptor, db, f),
                            ConditionalType = ConditionalType.Like,
                            FieldValue = search
                        }))
                    .ToList()
            };
            models.Add(or);
        }

        return models;
    }

    /// <summary>
    /// Translates one <see cref="ComparisonFilter"/> leaf. Extracted from <see cref="ToModel"/> so relation-filter
    /// pushdown (<c>FilterTranslator</c>) can build own-collection leaf conditionals — including on a synthetic
    /// <see cref="EntityDescriptor"/> for a junction/translation entity — without going through the full
    /// <see cref="FilterNode"/> AST dispatch. The result is one <see cref="ConditionalModel"/> or, for a set
    /// operator on a text column, a group; it is null when the operator adds no condition (an empty <c>_nin</c>).
    /// </summary>
    public static IConditionalModel? ToCondition(ComparisonFilter c, EntityDescriptor d, ISqlSugarClient db) =>
        ValueConditions.Build(Target(c.FieldPath, d, db), c.Op, c.Value);

    /// <summary>The terms a leaf contributes inside an <c>_or</c> group.</summary>
    internal static OrTerms ToOrTerms(ComparisonFilter c, EntityDescriptor d, ISqlSugarClient db) =>
        ValueConditions.OrMembers(Target(c.FieldPath, d, db), c.Op, c.Value);

    private static ValueTarget Target(string field, EntityDescriptor d, ISqlSugarClient db)
    {
        var property = d.EntityType
            .GetProperty(d.FieldToProperty.GetValueOrDefault(field) ?? string.Empty,
                BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
        var utcInstant = property is not null && UtcInstantConverter.IsUtcInstantProperty(property);
        return new ValueTarget(
            Column(d, db, field), field, property?.PropertyType ?? typeof(string),
            db.CurrentConnectionConfig.DbType, utcInstant);
    }

    private static IConditionalModel? ToModel(FilterNode node, EntityDescriptor d, ISqlSugarClient db)
    {
        switch (node)
        {
            case ComparisonFilter c:
                return ToCondition(c, d, db);

            case LogicalFilter { Op: LogicalOperator.Or } l:
                return OrGroup(l, d, db);

            case LogicalFilter l:
                return AndGroup(l, d, db);

            default:
                throw new InvalidOperationException($"Unknown filter node type: {node.GetType().Name}");
        }
    }

    // Nested LogicalFilter inside a LogicalFilter is not supported (SqlSugar's ConditionalCollections
    // cannot be nested), and neither is a text-column set operator inside an `_and` group here: its
    // group is itself a ConditionalCollections.
    private static IConditionalModel? AndGroup(LogicalFilter l, EntityDescriptor d, ISqlSugarClient db)
    {
        var members = l.Children.Select(ch => ToLeaf(ch, d, db)).OfType<ConditionalModel>().ToList();
        return members.Count == 0
            ? null
            : new ConditionalCollections
            {
                ConditionalList = members
                    .Select(m => new KeyValuePair<WhereType, ConditionalModel>(WhereType.And, m))
                    .ToList()
            };
    }

    private static IConditionalModel? ToLeaf(FilterNode node, EntityDescriptor d, ISqlSugarClient db)
    {
        if (node is not ComparisonFilter c)
            throw new InvalidOperationException(
                $"Nested logical filter reached the translator for node type '{node.GetType().Name}'; " +
                "this should have been rejected by QueryValidator before reaching this point.");

        var condition = ToCondition(c, d, db);
        return condition is null or ConditionalModel
            ? condition
            : throw new QueryException(
                $"'{c.Op}' on the text field '{c.FieldPath}' cannot be combined inside a logical group here.");
    }

    private static IConditionalModel OrGroup(LogicalFilter l, EntityDescriptor d, ISqlSugarClient db)
    {
        var terms = l.Children
            .Select(ch => ch is ComparisonFilter c
                ? ToOrTerms(c, d, db)
                : throw new InvalidOperationException(
                    $"Nested logical filter reached the translator for node type '{ch.GetType().Name}'; " +
                    "this should have been rejected by QueryValidator before reaching this point."))
            .ToList();
        var models = terms.SelectMany(t => t.Models).ToList();
        if (models.Count == 0 && terms.Select(t => t.NeverMatchesColumn).FirstOrDefault(c => c is not null) is { } never)
            return ValueConditions.NeverMatches(never);
        return new ConditionalCollections
        {
            ConditionalList = models
                .Select(m => new KeyValuePair<WhereType, ConditionalModel>(WhereType.Or, m))
                .ToList()
        };
    }

    private static string Column(EntityDescriptor d, ISqlSugarClient db, string field)
    {
        if (!d.FieldToProperty.TryGetValue(field, out var property))
            throw new ArgumentException(
                $"Unknown field path '{field}' for entity '{d.EntityType.Name}'.", nameof(field));

        return db.EntityMaintenance.GetDbColumnName(property, d.EntityType);
    }
}
