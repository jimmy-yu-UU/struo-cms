// src/Struo.Api/GraphQl/GraphQlQueryBuilder.cs
using Struo.Application.Query;
using Struo.Domain.Query;

namespace Struo.Api.GraphQl;

/// <summary>
/// Assembles the existing <see cref="QueryModel"/> from GraphQL resolver arguments and a pre-built
/// (possibly nested) <see cref="DeepSpec"/> — the caller (<see cref="CollectionResolvers"/>) walks the
/// client's selection tree into that tree; this method just threads it through unchanged. Limit/offset
/// default to 0 so the existing QueryValidator performs its DefaultLimit/MaxLimit clamping (single
/// source of truth for pagination bounds).
/// </summary>
public static class GraphQlQueryBuilder
{
    public static IReadOnlyList<SortField> ParseSort(IReadOnlyList<string>? sort)
    {
        if (sort is null || sort.Count == 0) return [];
        var result = new List<SortField>(sort.Count);
        foreach (var token in sort)
        {
            if (string.IsNullOrWhiteSpace(token)) continue;
            var desc = token.StartsWith('-');
            var field = desc ? token[1..] : token;
            result.Add(new SortField(field, desc));
        }
        return result;
    }

    public static QueryModel BuildQuery(
        IReadOnlyDictionary<string, object?>? filter,
        IReadOnlyList<string>? sort,
        int? limit,
        int? offset,
        string? search,
        DeepSpec? deep,
        string collection = "",
        Func<string, string, string?>? relationTarget = null,
        IReadOnlyList<string>? facets = null,
        IReadOnlyDictionary<string, object?>? aggregate = null)
    {
        return new QueryModel(
            Fields: null,
            Filter: FilterInputTranslator.Translate(filter, collection, relationTarget),
            Sort: ParseSort(sort),
            Limit: limit ?? 0,
            Offset: offset ?? 0,
            Search: string.IsNullOrWhiteSpace(search) ? null : search)
        {
            Deep = deep,
            Facets = facets,
            Aggregate = ParseAggregate(aggregate)
        };
    }

    /// <summary>
    /// Turns the <c>aggregate</c> argument's raw input-object dictionary (op key -&gt; list of field
    /// names) into an <see cref="AggregateSpec"/>. An op key not recognised by
    /// <see cref="QueryParser.AggregateOps"/> throws — unreachable through the GraphQL schema (the
    /// AggregateInput type only declares those five field names) but reachable from a caller that
    /// hands in an arbitrary dictionary directly, e.g. a unit test. A null/absent value, or a value
    /// that isn't itself an enumerable of strings, is skipped rather than treated as an error (mirrors
    /// how an absent argument is represented). Returns null when no op ends up with any field name.
    /// </summary>
    public static AggregateSpec? ParseAggregate(IReadOnlyDictionary<string, object?>? input)
    {
        if (input is null) return null;
        var fields = new Dictionary<AggregateOp, IReadOnlyList<string>>();
        foreach (var (key, value) in input)
        {
            if (!QueryParser.AggregateOps.TryGetValue(key, out var op))
                throw new QueryException($"Unknown aggregate op '{key}'.");
            if (value is not IEnumerable<object?> list) continue;
            var names = list.OfType<string>().ToList();
            if (names.Count > 0) fields[op] = names;
        }
        return fields.Count == 0 ? null : new AggregateSpec(fields);
    }
}
