using Struo.Application.Configuration;
using Struo.Application.Metadata;
using Struo.Application.Query;
using Struo.Domain.Query;

namespace Struo.Application.Search;

/// <summary>
/// Asks the <see cref="ISearchProvider"/> once per list request and, when it answers, writes the
/// parsed candidate ids onto <see cref="QueryModel.SearchCandidates"/>. The provider's output is a
/// trust boundary: SqlSugar renders the resulting <c>id IN (…)</c> as SQL literals, so every id is
/// parsed to the collection's PK CLR type here (Guid or an integer type — anything else is refused)
/// and the count is capped by <c>Query:MaxSearchCandidates</c>. Violations are the fork's provider
/// misbehaving, not user input, hence <see cref="InvalidOperationException"/> (500), never
/// <see cref="QueryException"/> (400).
/// </summary>
public sealed class SearchCandidateResolver(ISearchProvider provider, StruoQueryOptions options, IEntityRegistry registry)
{
    public async Task<QueryModel> ResolveAsync(
        string collection, QueryModel validated, IReadOnlyList<string> searchableFields, string queryLocale, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(validated.Search)) return validated;

        var outcome = await provider.SearchAsync(new SearchRequest(collection, validated.Search, queryLocale, searchableFields), ct);
        if (!outcome.Handled) return validated;

        var ids = outcome.Ids!;
        if (ids.Count > options.MaxSearchCandidates)
            throw new InvalidOperationException(
                $"ISearchProvider returned {ids.Count} candidate ids for collection '{collection}', above Query:MaxSearchCandidates ({options.MaxSearchCandidates}).");

        var pkType = PrimaryKeyType(collection);
        var parsed = ids.Select(id => Parse(id, pkType, collection)).Distinct().ToList();
        return validated with { SearchCandidates = parsed };
    }

    private Type PrimaryKeyType(string collection)
    {
        var d = registry.Get(collection) ?? throw new InvalidOperationException($"Unknown collection '{collection}'.");
        var declared = d.EntityType.GetProperty(d.IdProperty)!.PropertyType;
        var t = Nullable.GetUnderlyingType(declared) ?? declared;
        if (t == typeof(Guid) || t == typeof(long) || t == typeof(int) || t == typeof(short)) return t;
        throw new InvalidOperationException(
            $"Search candidates are only supported for Guid or integer primary keys; collection '{collection}' has a {t.Name} key.");
    }

    private static object Parse(string id, Type pkType, string collection)
    {
        try { return IdParsing.ParseTo(id, pkType); }
        catch (QueryException)
        {
            throw new InvalidOperationException(
                $"ISearchProvider returned '{id}', which is not a valid {pkType.Name} id for collection '{collection}'.");
        }
    }
}
