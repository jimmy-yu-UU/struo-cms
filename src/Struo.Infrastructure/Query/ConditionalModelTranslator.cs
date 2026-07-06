// src/Struo.Infrastructure/Query/ConditionalModelTranslator.cs
using System.Reflection;
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
                    FieldValue = ToFieldValue(c.Op, c.Value),
                    CSharpTypeName = ResolveCSharpTypeName(d, c.FieldPath, c.Op)
                };

            case LogicalFilter l:
                var wt = l.Op == LogicalOperator.And ? WhereType.And : WhereType.Or;
                return new ConditionalCollections
                {
                    ConditionalList = l.Children
                        .Select(ch => new KeyValuePair<WhereType, ConditionalModel>(wt, ToLeafModel(ch, d, db)))
                        .ToList()
                };

            default:
                throw new InvalidOperationException($"Unknown filter node type: {node.GetType().Name}");
        }
    }

    /// <summary>
    /// Converts a child node to a <see cref="ConditionalModel"/>.
    /// Nested <see cref="LogicalFilter"/> inside a <see cref="LogicalFilter"/> is not supported
    /// in Phase 2 (SqlSugar's ConditionalCollections cannot be nested).
    /// </summary>
    private static ConditionalModel ToLeafModel(FilterNode node, EntityDescriptor d, ISqlSugarClient db)
    {
        if (node is not ComparisonFilter)
            throw new InvalidOperationException(
                $"Nested logical filter reached the translator for node type '{node.GetType().Name}'; " +
                "this should have been rejected by QueryValidator before reaching this point.");

        return (ConditionalModel)ToModel(node, d, db);
    }

    private static string Column(EntityDescriptor d, ISqlSugarClient db, string field)
    {
        if (!d.FieldToProperty.TryGetValue(field, out var property))
            throw new ArgumentException(
                $"Unknown field path '{field}' for entity '{d.EntityType.Name}'.", nameof(field));

        return db.EntityMaintenance.GetDbColumnName(property, d.EntityType);
    }

    private static string? ToFieldValue(QueryOperator op, object? value)
    {
        // EqualNull (_null) / IsNot (_nnull) do not use FieldValue; always pass null.
        if (op is QueryOperator.Null or QueryOperator.NNull)
            return null;

        return value switch
        {
            null => null,
            System.Collections.IEnumerable list when value is not string =>
                string.Join(",", list.Cast<object?>().Select(v => v?.ToString())),
            _ => value.ToString()
        };
    }

    /// <summary>
    /// Resolves the SqlSugar <c>ConditionalModel.CSharpTypeName</c> for a filter field, so the
    /// string <c>FieldValue</c> is cast/parameterized to the column's real CLR type instead of
    /// being sent as text. Without this, a uuid (Guid) column comparison becomes
    /// <c>WHERE category_id = 'text-literal'</c>, which SQLite accepts (loose typing) but
    /// PostgreSQL rejects with 42883 "operator does not exist: uuid = text". Returns null for
    /// string columns (and anything unrecognized) so their behavior is unchanged.
    /// </summary>
    private static string? ResolveCSharpTypeName(EntityDescriptor d, string field, QueryOperator op)
    {
        // _null / _nnull never touch FieldValue (render pure "IS [NOT] NULL"); a cast is moot.
        if (op is QueryOperator.Null or QueryOperator.NNull)
            return null;

        var clrType = ResolveClrType(d, field);
        return clrType is null ? null : SqlSugarTypeName(clrType);
    }

    private static Type? ResolveClrType(EntityDescriptor d, string field)
    {
        if (!d.FieldToProperty.TryGetValue(field, out var property))
            return null;

        var propertyType = d.EntityType
            .GetProperty(property, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase)
            ?.PropertyType;

        return propertyType is null ? null : Nullable.GetUnderlyingType(propertyType) ?? propertyType;
    }

    /// <summary>
    /// Maps a CLR type to the string SqlSugar's <c>ConditionalModel.CSharpTypeName</c> expects
    /// (verified against the installed SqlSugarCore 5.1.4.215 via decompilation of
    /// <c>UtilMethods.ConvertDataByTypeName</c>/<c>IsNumber</c>, which compare case-insensitively
    /// against these literals and against <c>Type.Name</c>). Returns null for <see cref="string"/>
    /// and any unmapped type, so those columns keep today's untyped (string) behavior exactly.
    /// </summary>
    internal static string? SqlSugarTypeName(Type clrType)
    {
        if (clrType == typeof(Guid)) return "guid";
        if (clrType == typeof(int)) return "int";
        if (clrType == typeof(long)) return "long";
        if (clrType == typeof(short)) return "short";
        if (clrType == typeof(bool)) return "bool";
        if (clrType == typeof(DateTime)) return "datetime";
        if (clrType == typeof(DateTimeOffset)) return "datetimeoffset";
        if (clrType == typeof(decimal)) return "decimal";
        if (clrType == typeof(double)) return "double";
        if (clrType == typeof(float)) return "float";
        return null;
    }

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
        // Portable null checks. EqualNull renders pure "col IS NULL"; IsNot renders
        // pure "col IS NOT NULL". IsNullOrEmpty would emit "OR col = ''", which throws
        // a type error on PostgreSQL for non-text columns (bigint, timestamptz).
        QueryOperator.Null       => ConditionalType.EqualNull,
        QueryOperator.NNull      => ConditionalType.IsNot,
        _ => throw new InvalidOperationException($"Unmapped operator: {op}")
    };
}
