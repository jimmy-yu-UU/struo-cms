// src/Struo.Api/GraphQl/GraphQlDataSource.cs
using System.Text.Json;
using Struo.Application.Query;
using Struo.Domain.Query;

namespace Struo.Api.GraphQl;

/// <summary>
/// The seam GraphQL resolvers read and write items through, fakeable in tests. Covers the surface
/// GraphQL needs: reads (Query/Get), writes (Create/Update/Delete/Restore/Revert) and revisions. The
/// shipped implementation, <see cref="ItemUseCasesGraphQlDataSource"/>, delegates every call to the
/// registered <see cref="IItemUseCases"/>.
/// </summary>
public interface IGraphQlDataSource
{
    Task<PagedResult> QueryAsync(string collection, QueryModel query, string? locale, DeletedFilter deleted, CancellationToken ct);
    Task<IReadOnlyDictionary<string, object?>?> GetAsync(string collection, string id, DeepSpec? deep, string? locale, CancellationToken ct);
    Task<bool> DeleteAsync(string collection, string id, bool purge, CancellationToken ct);
    Task<IReadOnlyDictionary<string, object?>> CreateAsync(string collection, JsonElement body, CancellationToken ct);
    Task<IReadOnlyDictionary<string, object?>?> UpdateAsync(string collection, string id, JsonElement body, CancellationToken ct);
    Task<IReadOnlyDictionary<string, object?>?> RestoreAsync(string collection, string id, CancellationToken ct);
    Task<IReadOnlyList<Struo.Application.Revisions.RevisionInfo>?> ListRevisionsAsync(string collection, string id, CancellationToken ct);
    Task<Struo.Application.Revisions.RevisionRecord?> GetRevisionAsync(string collection, string id, long revisionNumber, CancellationToken ct);
    Task<IReadOnlyDictionary<string, object?>?> RevertAsync(string collection, string id, long revisionNumber, long? expectedVersion, CancellationToken ct);
}

/// <summary>Forwards each call to <see cref="IItemUseCases"/>, the same seam REST uses.</summary>
public sealed class ItemUseCasesGraphQlDataSource(IItemUseCases items) : IGraphQlDataSource
{
    public Task<PagedResult> QueryAsync(string collection, QueryModel query, string? locale, DeletedFilter deleted, CancellationToken ct)
        => items.QueryAsync(collection, query, locale, deleted, ct);

    public Task<IReadOnlyDictionary<string, object?>?> GetAsync(string collection, string id, DeepSpec? deep, string? locale, CancellationToken ct)
        => items.GetAsync(collection, id, deep, locale, ct: ct);

    public Task<bool> DeleteAsync(string collection, string id, bool purge, CancellationToken ct)
        => items.DeleteAsync(collection, id, purge, ct);

    public Task<IReadOnlyDictionary<string, object?>> CreateAsync(string collection, JsonElement body, CancellationToken ct)
        => items.CreateAsync(collection, body, ct);

    public Task<IReadOnlyDictionary<string, object?>?> UpdateAsync(string collection, string id, JsonElement body, CancellationToken ct)
        => items.UpdateAsync(collection, id, body, ct);

    public Task<IReadOnlyDictionary<string, object?>?> RestoreAsync(string collection, string id, CancellationToken ct)
        => items.RestoreAsync(collection, id, ct);

    public Task<IReadOnlyList<Struo.Application.Revisions.RevisionInfo>?> ListRevisionsAsync(string collection, string id, CancellationToken ct)
        => items.ListRevisionsAsync(collection, id, ct);

    public Task<Struo.Application.Revisions.RevisionRecord?> GetRevisionAsync(string collection, string id, long revisionNumber, CancellationToken ct)
        => items.GetRevisionAsync(collection, id, revisionNumber, ct);

    public Task<IReadOnlyDictionary<string, object?>?> RevertAsync(string collection, string id, long revisionNumber, long? expectedVersion, CancellationToken ct)
        => items.RevertAsync(collection, id, revisionNumber, expectedVersion, ct);
}
