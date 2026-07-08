// tests/Struo.Tests/GraphQl/FakeGraphQlDataSource.cs
using Struo.Api.GraphQl;
using Struo.Application.Query;
using Struo.Domain.Query;

namespace Struo.Tests.GraphQl;

/// <summary>
/// Test double for <see cref="IGraphQlDataSource"/>: canned results via settable delegates, plus a
/// call log so execution tests can assert which collection(s) were queried and how many times.
/// </summary>
internal sealed class FakeGraphQlDataSource : IGraphQlDataSource
{
    public List<string> QueryCollections { get; } = [];
    public int QueryCalls => QueryCollections.Count;

    public Func<string, QueryModel, string?, PagedResult> OnQuery { get; set; } =
        (_, q, _) => new PagedResult([], 0, q.Limit, q.Offset);

    public Func<string, string, DeepSpec?, string?, IReadOnlyDictionary<string, object?>?> OnGet { get; set; } =
        (_, _, _, _) => null;

    public List<string> DeletedCollections { get; } = [];
    public Func<string, string, bool> OnDelete { get; set; } = (_, _) => false;

    public List<string> CreatedCollections { get; } = [];
    public Func<string, System.Text.Json.JsonElement, IReadOnlyDictionary<string, object?>> OnCreate { get; set; } =
        (_, _) => new Dictionary<string, object?> { ["id"] = "1" };

    public Func<string, string, System.Text.Json.JsonElement, IReadOnlyDictionary<string, object?>?> OnUpdate { get; set; } =
        (_, _, _) => null;

    public Task<PagedResult> QueryAsync(string collection, QueryModel query, string? locale, CancellationToken ct)
    {
        QueryCollections.Add(collection);
        return Task.FromResult(OnQuery(collection, query, locale));
    }

    public Task<IReadOnlyDictionary<string, object?>?> GetAsync(
        string collection, string id, DeepSpec? deep, string? locale, CancellationToken ct)
        => Task.FromResult(OnGet(collection, id, deep, locale));

    public Task<bool> DeleteAsync(string collection, string id, CancellationToken ct)
    {
        DeletedCollections.Add(collection);
        return Task.FromResult(OnDelete(collection, id));
    }

    public Task<IReadOnlyDictionary<string, object?>> CreateAsync(
        string collection, System.Text.Json.JsonElement body, CancellationToken ct)
    {
        CreatedCollections.Add(collection);
        return Task.FromResult(OnCreate(collection, body));
    }

    public Task<IReadOnlyDictionary<string, object?>?> UpdateAsync(
        string collection, string id, System.Text.Json.JsonElement body, CancellationToken ct)
        => Task.FromResult(OnUpdate(collection, id, body));
}
