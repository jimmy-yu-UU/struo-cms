using SqlSugar;
using Struo.Application.Security;

namespace Struo.Infrastructure.Identity;

public sealed class SqlSugarRolePermissionStore(ISqlSugarClient db) : IRolePermissionStore
{
    public async Task<RolePermissionData> LoadForUserAsync(Guid? userId, CancellationToken ct = default)
    {
        List<Role> roles;
        if (userId is null)
        {
            roles = await db.Queryable<Role>().Where(r => r.Name == "public").ToListAsync(ct);
        }
        else
        {
            roles = await db.Queryable<UserRole>()
                .InnerJoin<Role>((ur, r) => ur.RoleId == r.Id)
                .Where((ur, r) => ur.UserId == userId.Value)
                .Select((ur, r) => r)
                .ToListAsync(ct);

            // Public floor: an authenticated user with no assigned roles inherits the public role
            // (still <= what anonymous callers can see). Assigned users get only their roles.
            if (roles.Count == 0)
                roles = await db.Queryable<Role>().Where(r => r.Name == "public").ToListAsync(ct);
        }

        if (roles.Count == 0)
            return new RolePermissionData([], []);

        var roleIds = roles.Select(r => r.Id).ToList();
        var perms = await db.Queryable<Permission>()
            .Where(p => roleIds.Contains(p.RoleId))
            .ToListAsync(ct);

        return new RolePermissionData(
            roles.Select(r => new RoleRow(r.Id, r.Name, r.IsSuperAdmin)).ToList(),
            perms.Select(p => new PermissionRow(p.RoleId, p.Collection, p.CanRead, p.CanWrite, p.CanDelete)).ToList());
    }

    public async Task<RolePermissionData> LoadForRolesAsync(
        IReadOnlyList<Guid> roleIds, CancellationToken ct = default)
    {
        List<Role> roles;
        if (roleIds.Count == 0)
        {
            // Public floor: an empty hypothetical set previews what a role-less user would get.
            roles = await db.Queryable<Role>().Where(r => r.Name == "public").ToListAsync(ct);
        }
        else
        {
            var ids = roleIds.ToList();
            roles = await db.Queryable<Role>().Where(r => ids.Contains(r.Id)).ToListAsync(ct);
        }

        if (roles.Count == 0)
            return new RolePermissionData([], []);

        var loadedIds = roles.Select(r => r.Id).ToList();
        var perms = await db.Queryable<Permission>()
            .Where(p => loadedIds.Contains(p.RoleId))
            .ToListAsync(ct);

        return new RolePermissionData(
            roles.Select(r => new RoleRow(r.Id, r.Name, r.IsSuperAdmin)).ToList(),
            perms.Select(p => new PermissionRow(p.RoleId, p.Collection, p.CanRead, p.CanWrite, p.CanDelete)).ToList());
    }
}
