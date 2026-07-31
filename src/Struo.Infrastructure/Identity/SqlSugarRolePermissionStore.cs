using SqlSugar;
using Struo.Application.Security;

namespace Struo.Infrastructure.Identity;

public sealed class SqlSugarRolePermissionStore(ISqlSugarClient db) : IRolePermissionStore
{
    /// <remarks>
    /// The <c>public</c> role is the permission FLOOR for every caller, not a fallback for the
    /// role-less: this model has no deny semantics (<see cref="PermissionResolver"/> folds grants with
    /// OR), so a role can only ADD to what <c>public</c> already exposes. Without the union a user WITH
    /// roles could read less than an anonymous visitor — logged in and worse off.
    /// </remarks>
    public async Task<RolePermissionData> LoadForUserAsync(Guid? userId, CancellationToken ct = default)
    {
        if (userId is null) return await ToDataAsync(await PublicRolesAsync(ct), ct);

        var roles = await db.Queryable<Role>()
            .Where(r => r.Name == RbacSeeder.PublicRoleName ||
                        SqlFunc.Subqueryable<UserRole>()
                            .Where(ur => ur.RoleId == r.Id && ur.UserId == userId.Value)
                            .Any())
            .ToListAsync(ct);

        return await ToDataAsync(roles.DistinctBy(r => r.Id).ToList(), ct);
    }

    /// <summary>Previews the effective permissions of a hypothetical role set. Unions the same
    /// <c>public</c> floor <see cref="LoadForUserAsync"/> applies, so the preview cannot disagree with
    /// what the request pipeline actually resolves.</summary>
    public async Task<RolePermissionData> LoadForRolesAsync(
        IReadOnlyList<Guid> roleIds, CancellationToken ct = default)
    {
        if (roleIds.Count == 0) return await ToDataAsync(await PublicRolesAsync(ct), ct);

        var ids = roleIds.ToList();
        var roles = await db.Queryable<Role>()
            .Where(r => r.Name == RbacSeeder.PublicRoleName || ids.Contains(r.Id))
            .ToListAsync(ct);

        return await ToDataAsync(roles.DistinctBy(r => r.Id).ToList(), ct);
    }

    private Task<List<Role>> PublicRolesAsync(CancellationToken ct) =>
        db.Queryable<Role>().Where(r => r.Name == RbacSeeder.PublicRoleName).ToListAsync(ct);

    private async Task<RolePermissionData> ToDataAsync(List<Role> roles, CancellationToken ct)
    {
        if (roles.Count == 0) return new RolePermissionData([], []);

        var roleIds = roles.Select(r => r.Id).ToList();
        var perms = await db.Queryable<Permission>()
            .Where(p => roleIds.Contains(p.RoleId))
            .ToListAsync(ct);

        return new RolePermissionData(
            roles.Select(r => new RoleRow(r.Id, r.Name, r.IsSuperAdmin)).ToList(),
            perms.Select(p => new PermissionRow(p.RoleId, p.Collection, p.CanRead, p.CanWrite, p.CanDelete)).ToList());
    }
}
