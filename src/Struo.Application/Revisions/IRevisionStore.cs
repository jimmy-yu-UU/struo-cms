namespace Struo.Application.Revisions;

/// <summary>Revision metadata (no snapshot payload) — for the newest-first history list.</summary>
public sealed record RevisionInfo(
    long RevisionNumber, string Operation, DateTime CreatedAt, Guid? CreatedBy, long? SourceRevisionNumber);

/// <summary>A single revision including its stored snapshot JSON.</summary>
public sealed record RevisionRecord(
    long RevisionNumber, string Operation, DateTime CreatedAt, Guid? CreatedBy, string Snapshot,
    long? SourceRevisionNumber);

/// <summary>
/// Storage for per-item revision snapshots. Backed by the framework `revisions` table.
/// Implementations run on the request-scoped SqlSugar client, so <see cref="CaptureAsync"/> called
/// inside <c>ItemService</c>'s write transaction commits atomically with the write it describes.
/// </summary>
public interface IRevisionStore
{
    /// Assigns the next per-(collection,itemId) RevisionNumber, stamps CreatedAt/By, inserts the snapshot.
    /// <paramref name="sourceRevisionNumber"/> is set only by the revert path, which records the
    /// revision whose snapshot it re-applied; every other write path leaves it null.
    Task CaptureAsync(string collection, string itemId, string operation, string snapshotJson,
        long? sourceRevisionNumber = null, CancellationToken ct = default);

    /// Newest-first metadata (no snapshot). Empty when the item has no revisions.
    Task<IReadOnlyList<RevisionInfo>> ListAsync(string collection, string itemId, CancellationToken ct = default);

    /// One revision incl. snapshot, or null when (collection,itemId,revisionNumber) has no row.
    Task<RevisionRecord?> GetAsync(string collection, string itemId, long revisionNumber, CancellationToken ct = default);

    /// <summary>
    /// Deletes every revision row for (collection, itemId) — called by purge so a
    /// permanently-deleted item does not leave orphaned, un-RBAC'd snapshot history behind. The
    /// default THROWS rather than silently no-ops: an implementation that forgot to override would
    /// otherwise leave orphaned snapshot history on purge with no failure signal — the exact defect
    /// class this default exists to eliminate. It still keeps pre-existing test doubles compiling; a
    /// double whose collection is actually purged must override it explicitly.
    /// </summary>
    Task DeleteForItemAsync(string collection, string itemId, CancellationToken ct = default) =>
        throw new NotSupportedException(
            "IRevisionStore.DeleteForItemAsync must be overridden by implementations that support purge integrity.");
}
