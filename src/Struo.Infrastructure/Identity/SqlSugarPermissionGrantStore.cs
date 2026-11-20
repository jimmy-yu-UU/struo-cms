// src/Struo.Infrastructure/Identity/SqlSugarPermissionGrantStore.cs
using SqlSugar;
using Struo.Application.Security;
using Struo.Domain.Query;
using Struo.Infrastructure.Query;

namespace Struo.Infrastructure.Identity;

public sealed class SqlSugarPermissionGrantStore(ISqlSugarClient db) : IPermissionGrantStore
{
    public Task<bool> RoleExistsAsync(Guid roleId, CancellationToken ct = default) =>
        db.Queryable<Role>().Where(r => r.Id == roleId).AnyAsync(ct);

    public async Task<IReadOnlyList<PermissionGrant>> ListAsync(Guid roleId, CancellationToken ct = default)
    {
        var rows = await db.Queryable<Permission>().Where(p => p.RoleId == roleId).ToListAsync(ct);
        return rows
            .Select(p => new PermissionGrant(p.Collection, p.CanRead, p.CanWrite, p.CanDelete))
            .ToList();
    }

    /// <summary>
    /// Delete-all + insert-all in one transaction, joining the caller's transaction when one is open. Observably identical to upsert-plus-prune and
    /// simpler: <c>Permission</c> rows are a hidden implementation detail, so their identity is not
    /// part of any contract and reusing ids buys nothing.
    /// </summary>
    public async Task<long?> ReplaceAsync(
        Guid roleId, IReadOnlyList<PermissionGrant> grants, long? expectedVersion = null,
        CancellationToken ct = default)
    {
        var rows = grants.Select(g => new Permission
        {
            Id = Guid.CreateVersion7(),
            RoleId = roleId,
            Collection = g.Collection,
            CanRead = g.CanRead,
            CanWrite = g.CanWrite,
            CanDelete = g.CanDelete,
        }).ToList();

        return await new TransactionRunner(db).InTransactionAsync<long?>(async () =>
        {
            // First statement of this save: the compare-and-increment locks the role row, so a concurrent
            // save of the same role waits here and then fails its version check.
            var newVersion = await AdvanceRoleVersionAsync(roleId, expectedVersion, ct);
            if (newVersion is null) return null;
            await db.Deleteable<Permission>().Where(p => p.RoleId == roleId).ExecuteCommandAsync(ct);
            if (rows.Count > 0) await db.Insertable(rows).ExecuteCommandAsync(ct);
            return newVersion;
        }, ct);
    }

    // One conditional UPDATE. Zero affected rows is either a role that no longer exists (null) or a version
    // that moved on (conflict), told apart by a read in the same transaction.
    // UpdatedAt/UpdatedBy stay as they are, since the role's own fields did not change.
    private async Task<long?> AdvanceRoleVersionAsync(Guid roleId, long? expectedVersion, CancellationToken ct)
    {
        var advance = db.Updateable<Role>().SetColumns(r => new Role { Version = r.Version + 1 });
        var affected = expectedVersion is { } expected
            ? await advance.Where(r => r.Id == roleId && r.Version == expected).ExecuteCommandAsync(ct)
            : await advance.Where(r => r.Id == roleId).ExecuteCommandAsync(ct);
        if (affected == 0)
        {
            if (!await db.Queryable<Role>().Where(r => r.Id == roleId).AnyAsync(ct)) return null;
            throw new ConcurrencyConflictException(
                "The record was modified by someone else since you loaded it. Reload and try again.");
        }
        return await db.Queryable<Role>().Where(r => r.Id == roleId).Select(r => r.Version).FirstAsync(ct);
    }

    public async Task DeleteForRoleAsync(Guid roleId, CancellationToken ct = default) =>
        await new TransactionRunner(db).InTransactionAsync(
            async () => await db.Deleteable<Permission>().Where(p => p.RoleId == roleId).ExecuteCommandAsync(ct), ct);
}
