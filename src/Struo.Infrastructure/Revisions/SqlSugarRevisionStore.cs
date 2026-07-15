using SqlSugar;
using Struo.Application.Abstractions;
using Struo.Application.Revisions;

namespace Struo.Infrastructure.Revisions;

public sealed class SqlSugarRevisionStore(ISqlSugarClient db, ICurrentUserAccessor currentUser) : IRevisionStore
{
    public async Task CaptureAsync(string collection, string itemId, string operation, string snapshotJson, CancellationToken ct = default)
    {
        // Next per-item sequence. Inside ItemService's write transaction the single-item write path is
        // serialized, so max+1 is race-free here. MaxAsync over no rows returns null -> 0.
        var max = await db.Queryable<Revision>()
            .Where(r => r.CollectionName == collection && r.ItemId == itemId)
            .MaxAsync(r => (long?)r.RevisionNumber);

        var row = new Revision
        {
            Id = Guid.CreateVersion7(),
            CollectionName = collection,
            ItemId = itemId,
            RevisionNumber = (max ?? 0) + 1,
            Operation = operation,
            Snapshot = snapshotJson,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = currentUser.GetCurrentUserId()
        };
        await db.Insertable(row).ExecuteCommandAsync();
    }

    public async Task<IReadOnlyList<RevisionInfo>> ListAsync(string collection, string itemId, CancellationToken ct = default)
    {
        var rows = await db.Queryable<Revision>()
            .Where(r => r.CollectionName == collection && r.ItemId == itemId)
            .OrderBy(r => r.RevisionNumber, OrderByType.Desc)
            .ToListAsync();
        return rows.Select(r => new RevisionInfo(r.RevisionNumber, r.Operation, r.CreatedAt, r.CreatedBy)).ToList();
    }

    public async Task<RevisionRecord?> GetAsync(string collection, string itemId, long revisionNumber, CancellationToken ct = default)
    {
        var r = await db.Queryable<Revision>()
            .Where(x => x.CollectionName == collection && x.ItemId == itemId && x.RevisionNumber == revisionNumber)
            .FirstAsync();
        return r is null ? null : new RevisionRecord(r.RevisionNumber, r.Operation, r.CreatedAt, r.CreatedBy, r.Snapshot);
    }
}
