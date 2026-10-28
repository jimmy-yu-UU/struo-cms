using SqlSugar;
using Struo.Application.Security;
using Struo.Infrastructure.Identity;

namespace Struo.Tests.Support.Seeding;

/// <summary>Test setup: seeds an initial admin when the <c>User</c> table is empty.</summary>
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
