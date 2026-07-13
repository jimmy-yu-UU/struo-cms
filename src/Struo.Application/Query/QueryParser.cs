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

        var model = new QueryModel(fields, filter, sort, limit, offset, search);
        return model with { Deep = ParseDeepEnvelope(env) };
    }

    private static DeepSpec? ParseDeepEnvelope(JsonElement env) =>
        env.TryGetProperty("deep", out var d) && d.ValueKind == JsonValueKind.Object
            ? ParseDeepObject(d)
            : null;

    private static DeepSpec? ParseDeepObject(JsonElement d)
    {
        var map = new Dictionary<string, DeepRelationSpec>(StringComparer.OrdinalIgnoreCase);
        foreach (var rel in d.EnumerateObject())
        {
            IReadOnlyList<string>? fields = null;
            int? limit = null;
            int? offset = null;
            FilterNode? filter = null;
            List<SortField>? sort = null;
            DeepSpec? nested = null;
            if (rel.Value.ValueKind == JsonValueKind.Object)
            {
                if (rel.Value.TryGetProperty("fields", out var f) && f.ValueKind == JsonValueKind.Array)
                    fields = f.EnumerateArray().Select(x => x.GetString() ?? "").ToList();
                if (rel.Value.TryGetProperty("filter", out var fl) && fl.ValueKind == JsonValueKind.Object)
                    filter = ParseFilter(fl);
                if (rel.Value.TryGetProperty("sort", out var s) && s.ValueKind == JsonValueKind.Array)
                {
                    sort = new List<SortField>();
                    foreach (var item in s.EnumerateArray())
                        sort.Add(ParseSortToken(item.GetString() ?? ""));
                }
                if (rel.Value.TryGetProperty("limit", out var l) && l.TryGetInt32(out var li)) limit = li;
                if (rel.Value.TryGetProperty("offset", out var o) && o.TryGetInt32(out var oi)) offset = oi;
                if (rel.Value.TryGetProperty("deep", out var nd) && nd.ValueKind == JsonValueKind.Object)
                    nested = ParseDeepObject(nd);
            }
            map[rel.Name] = new DeepRelationSpec(fields, limit, nested)
            {
                Filter = filter,
                Sort = sort,
                Offset = offset
            };
        }
        return map.Count == 0 ? null : new DeepSpec(map);
    }

    private static DeepSpec? ParseDeepQueryString(IReadOnlyDictionary<string, string?> query)
    {
        if (!query.TryGetValue("deep", out var dv) || string.IsNullOrWhiteSpace(dv)) return null;
        var map = dv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToDictionary(n => n, _ => new DeepRelationSpec(null, null), StringComparer.OrdinalIgnoreCase);
        return map.Count == 0 ? null : new DeepSpec(map);
    }

    private static FilterNode ParseFilter(JsonElement obj)
    {
        var nodes = new List<FilterNode>();

        foreach (var prop in obj.EnumerateObject())
        {
            if (prop.NameEquals("_and") || prop.NameEquals("_or"))
            {
                var op = prop.NameEquals("_and") ? LogicalOperator.And : LogicalOperator.Or;
                if (prop.Value.ValueKind != JsonValueKind.Array)
                    throw new QueryException($"'{prop.Name}' must be an array.");
                var children = prop.Value.EnumerateArray().Select(ParseFilter).ToList();
                nodes.Add(new LogicalFilter(op, children));
                continue;
            }

            var field = prop.Name;
            if (prop.Value.ValueKind != JsonValueKind.Object)
                throw new QueryException($"Filter for field '{field}' must be an object of operators.");

            var hasOperator = false;
            foreach (var inner in prop.Value.EnumerateObject())
            {
                if (!Operators.TryGetValue(inner.Name, out var qop))
                    throw new QueryException($"Unknown operator '{inner.Name}'.");
                nodes.Add(new ComparisonFilter(field, qop, ReadValue(inner.Value)));
                hasOperator = true;
            }
            if (!hasOperator)
                throw new QueryException($"Filter for field '{field}' has no operator.");
        }

        return nodes.Count switch
        {
            0 => throw new QueryException("Empty filter object."),
            1 => nodes[0],
            _ => new LogicalFilter(LogicalOperator.And, nodes)
        };
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

        var model = new QueryModel(fields, filter, sort, limit, offset, search);
        return model with { Deep = ParseDeepQueryString(query) };
    }
}
