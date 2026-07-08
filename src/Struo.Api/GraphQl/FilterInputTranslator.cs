// src/Struo.Api/GraphQl/FilterInputTranslator.cs
using Struo.Domain.Metadata.Enums;
using Struo.Domain.Query;

namespace Struo.Api.GraphQl;

/// <summary>
/// Translates a submitted GraphQL filter input (read back as a nested dictionary because the
/// input type's RuntimeType is a dictionary) into the existing <see cref="FilterNode"/> tree,
/// so the whole query then flows through the existing QueryValidator + repository unchanged.
/// </summary>
public static class FilterInputTranslator
{
    public static readonly IReadOnlyDictionary<string, QueryOperator> OperatorTokens =
        new Dictionary<string, QueryOperator>(StringComparer.Ordinal)
        {
            ["eq"] = QueryOperator.Eq, ["neq"] = QueryOperator.Neq,
            ["in"] = QueryOperator.In, ["nin"] = QueryOperator.Nin,
            ["lt"] = QueryOperator.Lt, ["lte"] = QueryOperator.Lte,
            ["gt"] = QueryOperator.Gt, ["gte"] = QueryOperator.Gte,
            ["contains"] = QueryOperator.Contains,
            ["startsWith"] = QueryOperator.StartsWith,
            ["endsWith"] = QueryOperator.EndsWith,
        };

    public static string OperatorInputTypeName(FieldInterface iface, Type? clrType) => iface switch
    {
        FieldInterface.Number or FieldInterface.Slider or FieldInterface.Rating => NumberFilter(clrType),
        FieldInterface.Boolean or FieldInterface.Checkbox => "BooleanFilter",
        FieldInterface.DateTime or FieldInterface.Date => "DateTimeFilter",
        FieldInterface.File or FieldInterface.Image or FieldInterface.Uuid => "IdFilter",
        _ => "StringFilter"
    };

    private static string NumberFilter(Type? clrType)
    {
        var t = clrType is null ? null : Nullable.GetUnderlyingType(clrType) ?? clrType;
        return (t == typeof(int) || t == typeof(short) || t == typeof(byte) || t == typeof(long))
            ? "IntFilter" : "FloatFilter";
    }

    public static FilterNode? Translate(IReadOnlyDictionary<string, object?>? filter)
    {
        if (filter is null || filter.Count == 0) return null;
        var children = new List<FilterNode>();

        foreach (var (key, value) in filter)
        {
            if (value is null) continue;
            if (string.Equals(key, "and", StringComparison.Ordinal))
                AddGroup(children, value, LogicalOperator.And);
            else if (string.Equals(key, "or", StringComparison.Ordinal))
                AddGroup(children, value, LogicalOperator.Or);
            else
                AddField(children, key, value);
        }

        if (children.Count == 0) return null;
        return children.Count == 1 ? children[0] : new LogicalFilter(LogicalOperator.And, children);
    }

    private static void AddGroup(List<FilterNode> into, object value, LogicalOperator op)
    {
        if (value is not System.Collections.IEnumerable list) return;
        var group = new List<FilterNode>();
        foreach (var item in list)
            if (AsDict(item) is { } d && Translate(d) is { } node) group.Add(node);
        if (group.Count > 0) into.Add(new LogicalFilter(op, group));
    }

    private static void AddField(List<FilterNode> into, string field, object value)
    {
        if (AsDict(value) is not { } ops) return;
        foreach (var (token, opValue) in ops)
        {
            // HotChocolate reads the nested op-input (e.g. StringFilter) back as a dictionary
            // populated with EVERY declared field, not just the ones the client set — unset
            // operators default to null here. Skip them, or a single `{ eq: x }` filter would
            // explode into one ComparisonFilter per operator (neq/in/contains/... all value=null),
            // ANDed together and silently corrupting the query.
            if (opValue is null) continue;

            if (string.Equals(token, "isNull", StringComparison.Ordinal))
            {
                var isNull = opValue is true;
                into.Add(new ComparisonFilter(field, isNull ? QueryOperator.Null : QueryOperator.NNull, null));
            }
            else if (OperatorTokens.TryGetValue(token, out var qop))
            {
                into.Add(new ComparisonFilter(field, qop, opValue));
            }
        }
    }

    private static IReadOnlyDictionary<string, object?>? AsDict(object? o)
    {
        if (o is IReadOnlyDictionary<string, object?> ro) return ro;
        if (o is IDictionary<string, object?> d) return new Dictionary<string, object?>(d);
        return null;
    }
}
