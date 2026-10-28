using SqlSugar;
using Struo.Infrastructure.Identity;

namespace Struo.Tests.Support.Seeding;

/// <summary>Test setup, idempotent. Seeds the <c>admin</c> (super) and <c>public</c> roles, assigns the
/// bootstrap admin user to <c>admin</c>, and grants the <c>public</c> role read on each given
/// collection.</summary>
public static class RbacSeeder
{
    public const string PublicRoleName = SqlSugarRolePermissionStore.PublicRoleName;

    public static async Task SeedAsync(
        ISqlSugarClient db, string? bootstrapAdminEmail,
        IEnumerable<string> publicReadCollections, CancellationToken ct = default)
    {
        var admin = await db.Queryable<Role>().Where(r => r.Name == "admin").FirstAsync(ct);
        if (admin is null)
        {
            admin = new Role { Id = Guid.CreateVersion7(), Name = "admin", IsSuperAdmin = true, Description = "Full access" };
            await db.Insertable(admin).ExecuteCommandAsync(ct);
        }

        var pub = await db.Queryable<Role>().Where(r => r.Name == PublicRoleName).FirstAsync(ct);
        if (pub is null)
        {
            pub = new Role { Id = Guid.CreateVersion7(), Name = PublicRoleName, IsSuperAdmin = false, Description = "Anonymous callers" };
            await db.Insertable(pub).ExecuteCommandAsync(ct);
        }

        if (!string.IsNullOrWhiteSpace(bootstrapAdminEmail))
        {
            var u = await db.Queryable<User>().Where(x => x.Email == bootstrapAdminEmail).FirstAsync(ct);
            if (u is not null)
            {
                var hasRole = await db.Queryable<UserRole>()
                    .Where(ur => ur.UserId == u.Id && ur.RoleId == admin.Id).AnyAsync(ct);
                if (!hasRole)
                    await db.Insertable(new UserRole { Id = Guid.CreateVersion7(), UserId = u.Id, RoleId = admin.Id })
                        .ExecuteCommandAsync(ct);
            }
        }

        foreach (var coll in publicReadCollections)
        {
            var exists = await db.Queryable<Permission>()
                .Where(p => p.RoleId == pub.Id && p.Collection == coll).AnyAsync(ct);
            if (!exists)
                await db.Insertable(new Permission
                {
                    Id = Guid.CreateVersion7(), RoleId = pub.Id, Collection = coll, CanRead = true
                }).ExecuteCommandAsync(ct);
        }
    }
}
