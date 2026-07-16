// src/Struo.Application/Query/IItemUseCases.cs
using System.Text.Json;
using Struo.Application.Revisions;
using Struo.Domain.Query;

namespace Struo.Application.Query;

/// <summary>
/// The item read/write use-case surface the HTTP layer depends on (ARC-6). Extracted verbatim from
/// <see cref="ItemService"/> so controllers bind to this seam rather than the concrete service,
/// keeping the Api layer testable in isolation and free of Infrastructure wiring. The sole
/// implementation is <see cref="ItemService"/>; GraphQL keeps its own <c>IGraphQlDataSource</c> seam.
/// </summary>
public interface IItemUseCases
{
    /// <summary>Reads a page of projected items for a collection. Requires read permission.</summary>
    Task<PagedResult> QueryAsync(
        string collection, QueryModel raw, string? locale = null,
        DeletedFilter deleted = DeletedFilter.Exclude, CancellationToken ct = default);

    /// <summary>Reads a single projected item by id, optionally expanding <paramref name="deep"/>
    /// relations. Requires read permission. Null for an unknown id (→ 404).</summary>
    Task<IReadOnlyDictionary<string, object?>?> GetAsync(
        string collection, string id, DeepSpec? deep = null, string? locale = null,
        DeletedFilter deleted = DeletedFilter.Exclude, CancellationToken ct = default);

    /// <summary>Creates an item (parent row + M2M junctions + translation sidecars committed
    /// atomically). Requires write permission.</summary>
    Task<IReadOnlyDictionary<string, object?>> CreateAsync(string collection, JsonElement body, CancellationToken ct = default);

    /// <summary>Merge-updates an item, overlaying only the fields the client actually sent, under
    /// optimistic-concurrency control. Requires write permission. Null for an unknown id (→ 404).</summary>
    Task<IReadOnlyDictionary<string, object?>?> UpdateAsync(string collection, string id, JsonElement body, CancellationToken ct = default);

    /// <summary>Deletes an item: soft-delete (trash) when the collection opts in and
    /// <paramref name="purge"/> is false, otherwise a permanent referential-integrity purge.
    /// Requires delete permission. Returns whether the row existed.</summary>
    Task<bool> DeleteAsync(string collection, string id, bool purge = false, CancellationToken ct = default);

    /// <summary>Restores a soft-deleted row and returns its re-read live projection. Requires delete
    /// permission. Null for an unknown id (→ 404); idempotent when already live.</summary>
    Task<IReadOnlyDictionary<string, object?>?> RestoreAsync(string collection, string id, CancellationToken ct = default);

    /// <summary>Newest-first revision metadata for an item. Requires read permission. Empty for a
    /// non-revisioned collection.</summary>
    Task<IReadOnlyList<RevisionInfo>> ListRevisionsAsync(string collection, string id, CancellationToken ct = default);

    /// <summary>A single revision including its (hidden-field-redacted) snapshot. Requires read
    /// permission. Null for an unknown revision or a non-revisioned collection (→ 404).</summary>
    Task<RevisionRecord?> GetRevisionAsync(string collection, string id, long revisionNumber, CancellationToken ct = default);

    /// <summary>Reverts an item to a past revision by re-applying its snapshot as a normal update
    /// (append-only). Requires write permission. Null for an unknown revision/item (→ 404).</summary>
    Task<IReadOnlyDictionary<string, object?>?> RevertAsync(string collection, string id, long revisionNumber, CancellationToken ct = default);
}
