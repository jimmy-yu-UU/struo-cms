// src/Struo.Infrastructure/Query/ConditionalModelTranslator.cs
using SqlSugar;
using Struo.Application.Metadata;
using Struo.Domain.Query;

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

        if (filter is not null)
            models.Add(ToModel(filter, descriptor, db));

        if (!string.IsNullOrWhiteSpace(search) && searchableFields.Count > 0)
        {
            var or = new ConditionalCollections
            {
                ConditionalList = searchableFields
                    .Select(f => new KeyValuePair<WhereType, ConditionalModel>(
                        WhereType.Or,
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

    private static IConditionalModel ToModel(FilterNode node, EntityDescriptor d, ISqlSugarClient db)
    {
        switch (node)
        {
            case ComparisonFilter c:
                return new ConditionalModel
                {
                    FieldName = Column(d, db, c.FieldPath),
                    ConditionalType = MapOperator(c.Op),
                    FieldValue = ToFieldValue(c.Value)
                };

            case LogicalFilter l:
                var wt = l.Op == LogicalOperator.And ? WhereType.And : WhereType.Or;
                return new ConditionalCollections
                {
                    ConditionalList = l.Children
                        .Select(ch => new KeyValuePair<WhereType, ConditionalModel>(
                            wt, (ConditionalModel)ToModel(ch, d, db)))
                        .ToList()
                };

            default:
                throw new InvalidOperationException($"Unknown filter node type: {node.GetType().Name}");
        }
    }

    private static string Column(EntityDescriptor d, ISqlSugarClient db, string field)
    {
        var property = d.FieldToProperty.TryGetValue(field, out var p) ? p : field;
        return db.EntityMaintenance.GetDbColumnName(property, d.EntityType);
    }

    private static string? ToFieldValue(object? value) => value switch
    {
        null => null,
        System.Collections.IEnumerable list when value is not string =>
            string.Join(",", list.Cast<object?>().Select(v => v?.ToString())),
        _ => value.ToString()
    };

    private static ConditionalType MapOperator(QueryOperator op) => op switch
    {
        QueryOperator.Eq         => ConditionalType.Equal,
        QueryOperator.Neq        => ConditionalType.NoEqual,
        QueryOperator.Contains   => ConditionalType.Like,
        QueryOperator.StartsWith => ConditionalType.LikeRight,  // LikeRight = value%
        QueryOperator.EndsWith   => ConditionalType.LikeLeft,   // LikeLeft  = %value
        QueryOperator.In         => ConditionalType.In,
        QueryOperator.Nin        => ConditionalType.NotIn,
        QueryOperator.Lt         => ConditionalType.LessThan,
        QueryOperator.Lte        => ConditionalType.LessThanOrEqual,
        QueryOperator.Gt         => ConditionalType.GreaterThan,
        QueryOperator.Gte        => ConditionalType.GreaterThanOrEqual,
        QueryOperator.Null       => ConditionalType.IsNullOrEmpty,
        QueryOperator.NNull      => ConditionalType.IsNot,
        _ => throw new InvalidOperationException($"Unmapped operator: {op}")
    };
}
