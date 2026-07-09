// src/Struo.Api/GraphQl/GraphQlQueryBuilder.cs
using Struo.Domain.Query;

namespace Struo.Api.GraphQl;

/// <summary>
/// Assembles the existing <see cref="QueryModel"/> from GraphQL resolver arguments and the set of
/// requested relation names. Limit/offset default to 0 so the existing QueryValidator performs its
/// DefaultLimit/MaxLimit clamping (single source of truth for pagination bounds).
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
        IReadOnlyList<string> requestedRelations,
        string collection = "",
        Func<string, string, string?>? relationTarget = null)
    {
        var deep = requestedRelations.Count == 0
            ? null
            : new DeepSpec(requestedRelations.ToDictionary(
                r => r, _ => new DeepRelationSpec(null, null), StringComparer.OrdinalIgnoreCase));

        return new QueryModel(
            Fields: null,
            Filter: FilterInputTranslator.Translate(filter, collection, relationTarget),
            Sort: ParseSort(sort),
            Limit: limit ?? 0,
            Offset: offset ?? 0,
            Search: string.IsNullOrWhiteSpace(search) ? null : search)
        {
            Deep = deep
        };
    }
}
