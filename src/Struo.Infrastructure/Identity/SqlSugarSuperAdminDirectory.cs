using SqlSugar;
using Struo.Application.Security;

namespace Struo.Infrastructure.Identity;

public sealed class SqlSugarSuperAdminDirectory(ISqlSugarClient db) : ISuperAdminDirectory
{
    public async Task<IReadOnlySet<Guid>> SuperAdminUserIdsAsync(
        IReadOnlyCollection<Guid> userIds, CancellationToken ct = default)
    {
        if (userIds.Count == 0) return new HashSet<Guid>();
        var ids = userIds.ToList();
        var roleIds = await Locked(db.Queryable<Role>().Where(r => r.IsSuperAdmin).Select(r => r.Id)).ToListAsync(ct);
        if (roleIds.Count == 0) return new HashSet<Guid>();

        var found = await Locked(db.Queryable<UserRole>().Where(ur => roleIds.Contains(ur.RoleId) && ids.Contains(ur.UserId))
            .Select(ur => ur.UserId)).ToListAsync(ct);
        return found.ToHashSet();
    }

    public async Task<IReadOnlySet<string>> SuperAdminEmailsAsync(
        IReadOnlyCollection<string> emails, CancellationToken ct = default)
    {
        if (emails.Count == 0) return new HashSet<string>(StringComparer.Ordinal);
        var values = emails.ToList();
        var found = await db.Queryable<User, UserRole, Role>((u, ur, r) => u.Id == ur.UserId && ur.RoleId == r.Id)
            .Where((u, ur, r) => r.IsSuperAdmin && values.Contains(SqlFunc.ToLower(u.Email)))
            .Select((u, ur, r) => SqlFunc.ToLower(u.Email))
            .ToListAsync(ct);
        return found.ToHashSet(StringComparer.Ordinal);
    }

    public async Task<IReadOnlySet<Guid>> SuperAdminRoleIdsAsync(
        IReadOnlyCollection<Guid> roleIds, CancellationToken ct = default)
    {
        if (roleIds.Count == 0) return new HashSet<Guid>();
        var ids = roleIds.ToList();
        var found = await db.Queryable<Role>()
            .Where(r => r.IsSuperAdmin && ids.Contains(r.Id))
            .Select(r => r.Id)
            .ToListAsync(ct);
        return found.ToHashSet();
    }

    // A typed update that leaves every column as it is: the database still locks the matched rows, on every
    // backend, until the transaction ends.
    public async Task LockSuperAdminRolesAsync(CancellationToken ct = default) =>
        await db.Updateable<Role>().SetColumns(r => r.Version == r.Version).Where(r => r.IsSuperAdmin)
            .ExecuteCommandAsync(ct);

    // Inside a transaction each read is a locking read (FOR UPDATE, or UPDLOCK on SQL Server), so it sees the
    // latest committed rows even where the transaction's snapshot is older (REPEATABLE READ on InnoDB), and a
    // concurrent change waits. SQLite has a single writer and needs no lock; outside a transaction a plain read
    // is all there is.
    public async Task<IReadOnlySet<Guid>> ActiveSuperAdminUserIdsAsync(CancellationToken ct = default)
    {
        var roleIds = await Locked(db.Queryable<Role>().Where(r => r.IsSuperAdmin).Select(r => r.Id)).ToListAsync(ct);
        if (roleIds.Count == 0) return new HashSet<Guid>();

        var memberIds = await Locked(db.Queryable<UserRole>().Where(ur => roleIds.Contains(ur.RoleId))
            .Select(ur => ur.UserId)).ToListAsync(ct);
        if (memberIds.Count == 0) return new HashSet<Guid>();

        var active = await Locked(db.Queryable<User>().Where(u => memberIds.Contains(u.Id) && u.IsActive)
            .Select(u => u.Id)).ToListAsync(ct);
        return active.ToHashSet();
    }

    private ISugarQueryable<T> Locked<T>(ISugarQueryable<T> query) =>
        db.Ado.IsAnyTran() && db.CurrentConnectionConfig.DbType != DbType.Sqlite
            ? query.TranLock(DbLockType.Wait)
            : query;
}
