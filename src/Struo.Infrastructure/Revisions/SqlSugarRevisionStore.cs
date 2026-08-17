using SqlSugar;
using Struo.Application.Abstractions;
using Struo.Application.Revisions;

namespace Struo.Infrastructure.Revisions;

public sealed class SqlSugarRevisionStore(ISqlSugarClient db, ICurrentUserAccessor currentUser) : IRevisionStore
{
    public async Task CaptureAsync(string collection, string itemId, string operation, string snapshotJson,
        long? sourceRevisionNumber = null, CancellationToken ct = default)
    {
        // Next per-item sequence. Inside ItemService's write transaction the single-item write path is
        // serialized, so max+1 is race-free here. MaxAsync over no rows returns null -> 0.
        var max = await db.Queryable<Revision>()
            .Where(r => r.CollectionName == collection && r.ItemId == itemId)
            .MaxAsync(r => (long?)r.RevisionNumber, ct);

        var row = new Revision
        {
            Id = Guid.CreateVersion7(),
            CollectionName = collection,
            ItemId = itemId,
            RevisionNumber = (max ?? 0) + 1,
            Operation = operation,
            Snapshot = snapshotJson,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = currentUser.GetCurrentUserId(),
            SourceRevisionNumber = sourceRevisionNumber
        };
        await db.Insertable(row).ExecuteCommandAsync(ct);
    }

    public async Task<IReadOnlyList<RevisionInfo>> ListAsync(string collection, string itemId, CancellationToken ct = default)
    {
        // Project only the metadata columns the list needs — the full `Snapshot` text column
        // (potentially large) is never read here; the detail path (GetAsync) fetches it separately,
        // and RevisionInfo never carried it in the first place, so the API response is unaffected.
        var rows = await db.Queryable<Revision>()
            .Where(r => r.CollectionName == collection && r.ItemId == itemId)
            .OrderBy(r => r.RevisionNumber, OrderByType.Desc)
            .Select(r => new { r.RevisionNumber, r.Operation, r.CreatedAt, r.CreatedBy, r.SourceRevisionNumber })
            .ToListAsync(ct);
        return rows
            .Select(r => new RevisionInfo(r.RevisionNumber, r.Operation, r.CreatedAt, r.CreatedBy, r.SourceRevisionNumber))
            .ToList();
    }

    public async Task<RevisionRecord?> GetAsync(string collection, string itemId, long revisionNumber, CancellationToken ct = default)
    {
        var r = await db.Queryable<Revision>()
            .Where(x => x.CollectionName == collection && x.ItemId == itemId && x.RevisionNumber == revisionNumber)
            .FirstAsync(ct);
        return r is null ? null
            : new RevisionRecord(r.RevisionNumber, r.Operation, r.CreatedAt, r.CreatedBy, r.Snapshot, r.SourceRevisionNumber);
    }

    public async Task DeleteForItemAsync(string collection, string itemId, CancellationToken ct = default) =>
        await db.Deleteable<Revision>()
            .Where(r => r.CollectionName == collection && r.ItemId == itemId)
            .ExecuteCommandAsync(ct);
}
