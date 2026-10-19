// src/Struo.Infrastructure/Identity/SqlSugarPermissionGrantStore.cs
using SqlSugar;
using Struo.Application.Security;
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
    public async Task ReplaceAsync(
        Guid roleId, IReadOnlyList<PermissionGrant> grants, CancellationToken ct = default)
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

        await new TransactionRunner(db).InTransactionAsync(async () =>
        {
            await db.Deleteable<Permission>().Where(p => p.RoleId == roleId).ExecuteCommandAsync(ct);
            if (rows.Count > 0) await db.Insertable(rows).ExecuteCommandAsync(ct);
        }, ct);
    }
}
