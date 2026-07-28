// src/Struo.Infrastructure/Identity/RbacSeeder.cs
using SqlSugar;

namespace Struo.Infrastructure.Identity;

/// <summary>Idempotent. Invoked by <see cref="Persistence.DataSeeder"/> only when the
/// <c>roles</c> table is created during startup (all environments). Seeds the <c>admin</c> (super)
/// and <c>public</c> roles, assigns the bootstrap admin user to <c>admin</c>, and grants the
/// <c>public</c> role read on each configured collection. The collection list is config (never a
/// <c>samples/*</c> reference).</summary>
public static class RbacSeeder
{
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

        var pub = await db.Queryable<Role>().Where(r => r.Name == "public").FirstAsync(ct);
        if (pub is null)
        {
            pub = new Role { Id = Guid.CreateVersion7(), Name = "public", IsSuperAdmin = false, Description = "Anonymous callers" };
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
