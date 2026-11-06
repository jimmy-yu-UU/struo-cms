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
        var found = await db.Queryable<UserRole, Role>((ur, r) => ur.RoleId == r.Id)
            .Where((ur, r) => r.IsSuperAdmin && ids.Contains(ur.UserId))
            .Select((ur, r) => ur.UserId)
            .ToListAsync(ct);
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

    public async Task<IReadOnlySet<Guid>> ActiveSuperAdminUserIdsAsync(CancellationToken ct = default)
    {
        var found = await db.Queryable<User, UserRole, Role>((u, ur, r) => u.Id == ur.UserId && ur.RoleId == r.Id)
            .Where((u, ur, r) => r.IsSuperAdmin && u.IsActive)
            .Select((u, ur, r) => u.Id)
            .ToListAsync(ct);
        return found.ToHashSet();
    }
}
