using SqlSugar;
using Struo.Application.Security;

namespace Struo.Infrastructure.Identity;

/// <summary>Invoked by <see cref="Persistence.DataSeeder"/> only when the <c>users</c> table is
/// created during startup (all environments); seeds an initial admin from config when the table is
/// empty.</summary>
public static class AdminUserSeeder
{
    public static async Task SeedAsync(ISqlSugarClient db, IPasswordHasher hasher, string? email, string? password)
    {
        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password)) return;
        if (await db.Queryable<User>().AnyAsync()) return;
        await db.Insertable(new User
        {
            Id = Guid.CreateVersion7(), Email = email, Password = hasher.Hash(password),
            Name = "Administrator", IsActive = true
        }).ExecuteCommandAsync();
    }
}
