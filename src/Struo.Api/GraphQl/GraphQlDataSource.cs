// src/Struo.Api/GraphQl/GraphQlDataSource.cs
using System.Text.Json;
using Struo.Application.Query;
using Struo.Domain.Query;

namespace Struo.Api.GraphQl;

/// <summary>
/// Api-owned seam over the concrete <see cref="ItemService"/>. Exists so GraphQL resolvers depend
/// on an interface (fakeable in tests) without adding an interface to the Application layer. Only
/// the operations GraphQL needs are exposed (read + delete so far; create/update land in later
/// mutation tasks).
/// </summary>
public interface IGraphQlDataSource
{
    Task<PagedResult> QueryAsync(string collection, QueryModel query, string? locale, CancellationToken ct);
    Task<IReadOnlyDictionary<string, object?>?> GetAsync(string collection, string id, DeepSpec? deep, string? locale, CancellationToken ct);
    Task<bool> DeleteAsync(string collection, string id, CancellationToken ct);
    Task<IReadOnlyDictionary<string, object?>> CreateAsync(string collection, JsonElement body, CancellationToken ct);
}

public sealed class ItemServiceGraphQlDataSource(ItemService items) : IGraphQlDataSource
{
    public Task<PagedResult> QueryAsync(string collection, QueryModel query, string? locale, CancellationToken ct)
        => items.QueryAsync(collection, query, locale, ct);

    public Task<IReadOnlyDictionary<string, object?>?> GetAsync(string collection, string id, DeepSpec? deep, string? locale, CancellationToken ct)
        => items.GetAsync(collection, id, deep, locale, ct);

    public Task<bool> DeleteAsync(string collection, string id, CancellationToken ct)
        => items.DeleteAsync(collection, id, ct);

    public Task<IReadOnlyDictionary<string, object?>> CreateAsync(string collection, JsonElement body, CancellationToken ct)
        => items.CreateAsync(collection, body, ct);
}
