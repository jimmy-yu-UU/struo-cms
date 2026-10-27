// src/Struo.Application/Query/Write/ICollectionDeleteHandler.cs
namespace Struo.Application.Query;

/// <summary>
/// Replaces the generic trash and purge of the collections it applies to, for a collection whose delete
/// has side effects the generic pipeline cannot express (a blob to remove, a reference to clear).
/// Register with <c>services.AddScoped&lt;ICollectionDeleteHandler, MyHandler&gt;()</c>; the first handler
/// that applies wins, and the core's <c>file</c> handler is registered inside <c>AddStruoCms()</c>, so
/// one registered after it never runs for <c>file</c>.
/// <see cref="ItemService"/> checks permissions and the id's existence first, runs the
/// interceptors' <see cref="IItemWriteInterceptor.BeforeWriteAsync"/>, then calls the handler, which owns
/// the transaction and the change notification from there.
/// </summary>
public interface ICollectionDeleteHandler
{
    /// <summary>True when this handler deletes rows of <paramref name="collection"/>, the canonical collection name.</summary>
    bool AppliesTo(string collection);

    /// <summary>
    /// False when <paramref name="id"/> cannot name a row of this collection, so the delete reports not
    /// found without reading it (the generic read rejects a malformed id with a 400). Defaults to true.
    /// </summary>
    bool IsValidId(string id) => true;

    /// <summary>Soft-deletes the row. True when the row exists; an already-trashed row is idempotent success.</summary>
    Task<bool> TrashAsync(string id, CancellationToken ct);

    /// <summary>Permanently removes the row. True when it existed.</summary>
    Task<bool> PurgeAsync(string id, CancellationToken ct);
}
