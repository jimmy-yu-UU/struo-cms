using SqlSugar;
using Struo.Application.Security;
using Struo.Infrastructure.Query;

namespace Struo.Infrastructure.Identity;

public sealed class SqlSugarRoleCapabilityStore(ISqlSugarClient db) : IRoleCapabilityStore
{
    public async Task<IReadOnlyList<string>> ListAsync(Guid roleId, CancellationToken ct = default)
    {
        var rows = await db.Queryable<RoleCapability>().Where(c => c.RoleId == roleId).ToListAsync(ct);
        return rows.Select(c => c.Capability).ToList();
    }

    /// <summary>Delete-all + insert-all in one transaction, joining the caller's transaction when one is
    /// open. The rows are an implementation detail, so reusing their ids buys nothing.</summary>
    public async Task ReplaceAsync(Guid roleId, IReadOnlyList<string> codes, CancellationToken ct = default)
    {
        var rows = codes.Select(code => new RoleCapability
        {
            Id = Guid.CreateVersion7(),
            RoleId = roleId,
            Capability = code,
        }).ToList();

        await new TransactionRunner(db).InTransactionAsync(async () =>
        {
            await db.Deleteable<RoleCapability>().Where(c => c.RoleId == roleId).ExecuteCommandAsync(ct);
            if (rows.Count > 0) await db.Insertable(rows).ExecuteCommandAsync(ct);
        }, ct);
    }
}
