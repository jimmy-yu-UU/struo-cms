// src/Struo.Application/Query/QueryParser.cs
using System.Collections.Frozen;
using System.Text.Json;
using Struo.Domain.Query;

namespace Struo.Application.Query;

public static class QueryParser
{
    private static readonly Dictionary<string, QueryOperator> Operators =
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

    // The body and query-string keys that more than one place reads.
    private const string KeyFields = "fields";
    private const string KeyLimit = "limit";
    private const string KeyOffset = "offset";
    private const string KeyFilter = "filter";
    private const string KeyDeep = "deep";
    private const string KeySort = "sort";

    private static readonly FrozenDictionary<QueryOperator, string> Tokens =
        Operators.ToFrozenDictionary(pair => pair.Value, pair => pair.Key);

    /// <summary>The spelling of an operator in the query string and in JSON, the inverse of the parser's own table.</summary>
    public static string TokenOf(QueryOperator op) => Tokens.TryGetValue(op, out var token) ? token : op.ToString();

    public static readonly IReadOnlyDictionary<string, AggregateOp> AggregateOps =
        new Dictionary<string, AggregateOp>(StringComparer.Ordinal)
        {
            ["count"] = AggregateOp.Count, ["sum"] = AggregateOp.Sum,
            ["min"] = AggregateOp.Min, ["max"] = AggregateOp.Max, ["avg"] = AggregateOp.Avg,
        };

    private static string[] SplitList(string value) =>
        value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    // Unlike SplitList above, this deliberately KEEPS a blank entry (e.g. "status,,tags" ->
    // ["status", "", "tags"]) so QueryValidator's per-entry "Facet path must not be empty." check can
    // reject it with a 400 — matching the manuals (docs/guide/{en,zh-TW}/11-query-advanced.md) and DSL
    // spec §6. SplitList itself stays RemoveEmptyEntries-tolerant for the aggregate field lists (e.g.
    // "aggregate[sum]=price,"), which is intentionally more forgiving and must not change here.
    private static string[] SplitFacetsList(string value) =>
        value.Split(',', StringSplitOptions.TrimEntries);

    private static AggregateOp ParseAggregateOp(string token) =>
        AggregateOps.TryGetValue(token, out var op) ? op : throw new QueryException($"Unknown aggregate op '{token}'.");

    private static string[]? ParseFacetsQueryString(IReadOnlyDictionary<string, string?> query) =>
        query.TryGetValue("facets", out var fv) && !string.IsNullOrWhiteSpace(fv) ? SplitFacetsList(fv) : null;

    private static AggregateSpec? ParseAggregateQueryString(IReadOnlyDictionary<string, string?> query)
    {
        var fields = new Dictionary<AggregateOp, IReadOnlyList<string>>();
        foreach (var (key, val) in query)
        {
            if (!key.StartsWith("aggregate", StringComparison.Ordinal)) continue;
            if (!key.StartsWith("aggregate[", StringComparison.Ordinal) || !key.EndsWith(']'))
                throw new QueryException($"Malformed aggregate key '{key}'.");
            var op = ParseAggregateOp(key["aggregate[".Length..^1]);
            if (string.IsNullOrWhiteSpace(val)) continue;
            fields[op] = SplitList(val);
        }
        return fields.Count == 0 ? null : new AggregateSpec(fields);
    }

    // A body key that is missing or JSON null is not given.
    private static bool Given(JsonElement owner, string name, out JsonElement value) =>
        owner.TryGetProperty(name, out value) && value.ValueKind != JsonValueKind.Null;

    private static List<string>? ParseFacetsEnvelope(JsonElement env)
    {
        if (!Given(env, "facets", out var f)) return null;
        if (f.ValueKind != JsonValueKind.Array || f.EnumerateArray().Any(x => x.ValueKind != JsonValueKind.String))
            throw new QueryException("'facets' must be an array of strings.");
        return f.EnumerateArray().Select(x => x.GetString()!).ToList();
    }

    private static AggregateSpec? ParseAggregateEnvelope(JsonElement env)
    {
        if (!Given(env, "aggregate", out var a)) return null;
        if (a.ValueKind != JsonValueKind.Object) throw new QueryException("'aggregate' must be an object.");
        var fields = new Dictionary<AggregateOp, IReadOnlyList<string>>();
        foreach (var prop in a.EnumerateObject())
        {
            var op = ParseAggregateOp(prop.Name);
            if (prop.Value.ValueKind != JsonValueKind.Array || prop.Value.EnumerateArray().Any(x => x.ValueKind != JsonValueKind.String))
                throw new QueryException($"'aggregate.{prop.Name}' must be an array of strings.");
            var list = prop.Value.EnumerateArray().Select(x => x.GetString()!).ToList();
            if (list.Count > 0) fields[op] = list;
        }
        return fields.Count == 0 ? null : new AggregateSpec(fields);
    }

    // A body field of the wrong JSON kind is a QueryException naming the field; reading it with
    // GetString or TryGetInt32 would throw InvalidOperationException, which answers 500.
    private static string Where(string? relation) => relation is null ? "" : $" (in 'deep.{relation}')";

    private static List<string> ReadStrings(JsonElement array, string name, string? relation = null) =>
        array.EnumerateArray()
            .Select(x => x.ValueKind == JsonValueKind.String
                ? x.GetString()!
                : throw new QueryException($"'{name}' must be an array of strings{Where(relation)}."))
            .ToList();

    private static int ReadInt(JsonElement value, string name, string? relation = null)
    {
        if (value.ValueKind != JsonValueKind.Number)
            throw new QueryException($"'{name}' must be an integer{Where(relation)}.");
        return value.TryGetInt32(out var number) ? number : 0;
    }

    private static string? ReadSearch(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.String => value.GetString(),
        JsonValueKind.Null => null,
        _ => throw new QueryException("'search' must be a string."),
    };

    // A key that shapes the result is read only as its own kind. JSON null means the key is not given;
    // any other kind is a QueryException, because ignoring it would hand back an unfiltered result.
    private static bool IsGiven(JsonElement owner, string name, JsonValueKind kind, string expected, string? relation,
        out JsonElement value)
    {
        if (!Given(owner, name, out value)) return false;
        if (value.ValueKind != kind)
            throw new QueryException($"'{name}' must be {expected}{Where(relation)}.");
        return true;
    }

    private static List<string>? ReadStringsKey(JsonElement owner, string name, string? relation = null) =>
        IsGiven(owner, name, JsonValueKind.Array, "an array of strings", relation, out var array)
            ? ReadStrings(array, name, relation)
            : null;

    private static FilterNode? ReadFilterKey(JsonElement owner, string? relation = null) =>
        IsGiven(owner, KeyFilter, JsonValueKind.Object, "an object", relation, out var filter) ? ParseFilter(filter) : null;

    public static QueryModel ParseEnvelope(JsonElement env)
    {
        if (env.ValueKind != JsonValueKind.Object)
            throw new QueryException("'body' must be a JSON object.");

        IReadOnlyList<string>? fields = ReadStringsKey(env, KeyFields);
        FilterNode? filter = ReadFilterKey(env);

        var sort = new List<SortField>();
        foreach (var item in ReadStringsKey(env, KeySort) ?? [])
            sort.Add(ParseSortToken(item));

        var limit = Given(env, KeyLimit, out var l) ? ReadInt(l, KeyLimit) : 0;
        var offset = Given(env, KeyOffset, out var o) ? ReadInt(o, KeyOffset) : 0;
        var search = env.TryGetProperty("search", out var se) ? ReadSearch(se) : null;

        var model = new QueryModel(fields, filter, sort, limit, offset, search);
        return model with { Deep = ParseDeepEnvelope(env), Facets = ParseFacetsEnvelope(env), Aggregate = ParseAggregateEnvelope(env) };
    }

    private static DeepSpec? ParseDeepEnvelope(JsonElement env) =>
        IsGiven(env, KeyDeep, JsonValueKind.Object, "an object", null, out var d) ? ParseDeepObject(d, null) : null;

    // path names the relation chain of a nested deep object, as in 'deep.articles.deep.tags'.
    private static DeepSpec? ParseDeepObject(JsonElement d, string? path)
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
            var here = path is null ? rel.Name : path + ".deep." + rel.Name;
            if (rel.Value.ValueKind is not (JsonValueKind.Object or JsonValueKind.Null))
                throw new QueryException($"'deep.{here}' must be an object.");
            if (rel.Value.ValueKind == JsonValueKind.Object)
            {
                fields = ReadStringsKey(rel.Value, KeyFields, here);
                filter = ReadFilterKey(rel.Value, here);
                if (ReadStringsKey(rel.Value, KeySort, here) is { } sortTokens)
                    sort = sortTokens.Select(ParseSortToken).ToList();
                if (Given(rel.Value, KeyLimit, out var l)) limit = ReadInt(l, KeyLimit, here);
                if (Given(rel.Value, KeyOffset, out var o)) offset = ReadInt(o, KeyOffset, here);
                if (IsGiven(rel.Value, KeyDeep, JsonValueKind.Object, "an object", here, out var nd))
                    nested = ParseDeepObject(nd, here);
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
        if (!query.TryGetValue(KeyDeep, out var dv) || string.IsNullOrWhiteSpace(dv)) return null;
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
                var children = prop.Value.EnumerateArray()
                    .Select(child => child.ValueKind == JsonValueKind.Object
                        ? ParseFilter(child)
                        : throw new QueryException($"'{prop.Name}' must be an array of objects."))
                    .ToList();
                nodes.Add(new LogicalFilter(op, children));
                continue;
            }

            var field = prop.Name;
            if (prop.Value.ValueKind != JsonValueKind.Object)
                throw new QueryException($"Filter for field '{field}' must be an object of operators.");

            ParseFieldObject(field, prop.Value, nodes);
        }

        return nodes.Count switch
        {
            0 => throw new QueryException("Empty filter object."),
            1 => nodes[0],
            _ => new LogicalFilter(LogicalOperator.And, nodes)
        };
    }

    private static void ParseFieldObject(string field, JsonElement obj, List<FilterNode> nodes)
    {
        var hasOperator = false;
        var hasQuantifier = false;
        foreach (var inner in obj.EnumerateObject())
        {
            if (FilterReservedTokens.TryQuantifier(inner.Name, out var quantifier))
            {
                if (inner.Value.ValueKind != JsonValueKind.Object || !inner.Value.EnumerateObject().Any())
                    throw new QueryException($"'{field}.{inner.Name}' must be a non-empty filter object.");
                nodes.Add(new RelationPredicateFilter(field, quantifier, ParseFilter(inner.Value)));
                hasQuantifier = true;
                continue;
            }
            if (!Operators.TryGetValue(inner.Name, out var qop))
                throw new QueryException($"Unknown operator '{inner.Name}'.");
            nodes.Add(new ComparisonFilter(field, qop, ReadValue(field, inner.Name, inner.Value)));
            hasOperator = true;
        }
        if (hasQuantifier && hasOperator)
            throw new QueryException($"'{field}' mixes a relation quantifier with scalar operators; a relation path has no scalar operators.");
        if (!hasOperator && !hasQuantifier)
            throw new QueryException($"Filter for field '{field}' has no operator.");
    }

    // A JSON object is no filter value, alone or as an element: it is refused here because the value
    // leaves this method as plain objects and text, so no later step could tell an object from a string.
    private static object? ReadValue(string field, string op, JsonElement v) => v.ValueKind switch
    {
        JsonValueKind.String => v.GetString(),
        JsonValueKind.Number => v.TryGetInt64(out var n) ? n : v.GetDouble(),
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        JsonValueKind.Null => null,
        JsonValueKind.Array => v.EnumerateArray().Select(element => ReadValue(field, op, element)).ToList(),
        _ => throw new QueryException(
            $"'{op}' on field '{field}' does not take a JSON object; send a string, a number, a boolean or a list of them.")
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
        IReadOnlyList<string>? fields = query.TryGetValue(KeyFields, out var fv) && !string.IsNullOrWhiteSpace(fv)
            ? fv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            : null;

        var sort = new List<SortField>();
        if (query.TryGetValue(KeySort, out var sv) && !string.IsNullOrWhiteSpace(sv))
            foreach (var t in sv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                sort.Add(ParseSortToken(t));

        var conditions = new List<ComparisonFilter>();
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

        var folded = RelationQuantifierFolder.Fold(conditions);
        FilterNode? filter = folded.Count switch
        {
            0 => null,
            1 => folded[0],
            _ => new LogicalFilter(LogicalOperator.And, folded)
        };

        var limit = query.TryGetValue(KeyLimit, out var lv) && int.TryParse(lv, out var li) ? li : 0;
        var offset = query.TryGetValue(KeyOffset, out var ov) && int.TryParse(ov, out var oi) ? oi : 0;
        var search = query.TryGetValue("search", out var se) ? se : null;

        var model = new QueryModel(fields, filter, sort, limit, offset, search);
        return model with { Deep = ParseDeepQueryString(query), Facets = ParseFacetsQueryString(query), Aggregate = ParseAggregateQueryString(query) };
    }
}
