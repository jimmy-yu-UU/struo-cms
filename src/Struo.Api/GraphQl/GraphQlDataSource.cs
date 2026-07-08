// src/Struo.Api/GraphQl/GraphQlDataSource.cs
using Struo.Application.Query;
using Struo.Domain.Query;

namespace Struo.Api.GraphQl;

/// <summary>
/// Api-owned read seam over the concrete <see cref="ItemService"/>. Exists so GraphQL resolvers
/// depend on an interface (fakeable in tests) without adding an interface to the Application layer.
/// Read-only: only the two read methods GraphQL needs are exposed.
/// </summary>
public interface IGraphQlDataSource
{
    Task<PagedResult> QueryAsync(string collection, QueryModel query, string? locale, CancellationToken ct);
    Task<IReadOnlyDictionary<string, object?>?> GetAsync(string collection, string id, DeepSpec? deep, string? locale, CancellationToken ct);
}

public sealed class ItemServiceGraphQlDataSource(ItemService items) : IGraphQlDataSource
{
    public Task<PagedResult> QueryAsync(string collection, QueryModel query, string? locale, CancellationToken ct)
        => items.QueryAsync(collection, query, locale, ct);

    public Task<IReadOnlyDictionary<string, object?>?> GetAsync(string collection, string id, DeepSpec? deep, string? locale, CancellationToken ct)
        => items.GetAsync(collection, id, deep, locale, ct);
}
