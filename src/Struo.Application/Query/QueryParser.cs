// src/Struo.Application/Query/QueryParser.cs
using System.Text.Json;
using Struo.Domain.Query;

namespace Struo.Application.Query;

public static class QueryParser
{
    private static readonly IReadOnlyDictionary<string, QueryOperator> Operators =
        new Dictionary<string, QueryOperator>(StringComparer.Ordinal)
        {
            ["_eq"] = QueryOperator.Eq, ["_neq"] = QueryOperator.Neq,
            ["_in"] = QueryOperator.In, ["_nin"] = QueryOperator.Nin,
            ["_lt"] = QueryOperator.Lt, ["_lte"] = QueryOperator.Lte,
            ["_gt"] = QueryOperator.Gt, ["_gte"] = QueryOperator.Gte,
            ["_null"] = QueryOperator.Null, ["_nnull"] = QueryOperator.NNull,
            ["_contains"] = QueryOperator.Contains,
            ["_starts_with"] = QueryOperator.StartsWith,
            ["_ends_with"] = QueryOperator.EndsWith,
        };

    public static QueryModel ParseEnvelope(JsonElement env)
    {
        IReadOnlyList<string>? fields = null;
        if (env.TryGetProperty("fields", out var f) && f.ValueKind == JsonValueKind.Array)
            fields = f.EnumerateArray().Select(x => x.GetString() ?? "").ToList();

        FilterNode? filter = null;
        if (env.TryGetProperty("filter", out var fl) && fl.ValueKind == JsonValueKind.Object)
            filter = ParseFilter(fl);

        var sort = new List<SortField>();
        if (env.TryGetProperty("sort", out var s) && s.ValueKind == JsonValueKind.Array)
            foreach (var item in s.EnumerateArray())
                sort.Add(ParseSortToken(item.GetString() ?? ""));

        var limit = env.TryGetProperty("limit", out var l) && l.TryGetInt32(out var li) ? li : 0;
        var offset = env.TryGetProperty("offset", out var o) && o.TryGetInt32(out var oi) ? oi : 0;
        var search = env.TryGetProperty("search", out var se) ? se.GetString() : null;

        return new QueryModel(fields, filter, sort, limit, offset, search);
    }

    private static FilterNode ParseFilter(JsonElement obj)
    {
        foreach (var prop in obj.EnumerateObject())
        {
            if (prop.NameEquals("_and") || prop.NameEquals("_or"))
            {
                var op = prop.NameEquals("_and") ? LogicalOperator.And : LogicalOperator.Or;
                if (prop.Value.ValueKind != JsonValueKind.Array)
                    throw new QueryException($"'{prop.Name}' must be an array.");
                var children = prop.Value.EnumerateArray().Select(ParseFilter).ToList();
                return new LogicalFilter(op, children);
            }

            var field = prop.Name;
            if (prop.Value.ValueKind != JsonValueKind.Object)
                throw new QueryException($"Filter for field '{field}' must be an object of operators.");
            var inner = prop.Value.EnumerateObject().FirstOrDefault();
            if (inner.Value.ValueKind == JsonValueKind.Undefined)
                throw new QueryException($"Filter for field '{field}' has no operator.");
            if (!Operators.TryGetValue(inner.Name, out var qop))
                throw new QueryException($"Unknown operator '{inner.Name}'.");
            return new ComparisonFilter(field, qop, ReadValue(inner.Value));
        }
        throw new QueryException("Empty filter object.");
    }

    private static object? ReadValue(JsonElement v) => v.ValueKind switch
    {
        JsonValueKind.String => v.GetString(),
        JsonValueKind.Number => v.TryGetInt64(out var n) ? n : v.GetDouble(),
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        JsonValueKind.Null => null,
        JsonValueKind.Array => v.EnumerateArray().Select(ReadValue).ToList(),
        _ => v.GetRawText()
    };

    private static SortField ParseSortToken(string token)
    {
        token = token.Trim();
        return token.StartsWith('-')
            ? new SortField(token[1..], true)
            : new SortField(token, false);
    }

    public static QueryModel ParseQueryString(IReadOnlyDictionary<string, string?> query)
    {
        IReadOnlyList<string>? fields = query.TryGetValue("fields", out var fv) && !string.IsNullOrWhiteSpace(fv)
            ? fv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            : null;

        var sort = new List<SortField>();
        if (query.TryGetValue("sort", out var sv) && !string.IsNullOrWhiteSpace(sv))
            foreach (var t in sv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                sort.Add(ParseSortToken(t));

        var conditions = new List<FilterNode>();
        foreach (var (key, val) in query)
        {
            if (!key.StartsWith("filter[", StringComparison.Ordinal)) continue;
            var inner = key["filter[".Length..].TrimEnd(']');
            var parts = inner.Split("][", StringSplitOptions.None);
            if (parts.Length != 2) throw new QueryException($"Malformed filter key '{key}'.");
            var field = parts[0];
            var opToken = parts[1];
            if (!Operators.TryGetValue(opToken, out var qop))
                throw new QueryException($"Unknown operator '{opToken}'.");
            conditions.Add(new ComparisonFilter(field, qop, val));
        }

        FilterNode? filter = conditions.Count switch
        {
            0 => null,
            1 => conditions[0],
            _ => new LogicalFilter(LogicalOperator.And, conditions)
        };

        var limit = query.TryGetValue("limit", out var lv) && int.TryParse(lv, out var li) ? li : 0;
        var offset = query.TryGetValue("offset", out var ov) && int.TryParse(ov, out var oi) ? oi : 0;
        var search = query.TryGetValue("search", out var se) ? se : null;

        return new QueryModel(fields, filter, sort, limit, offset, search);
    }
}
