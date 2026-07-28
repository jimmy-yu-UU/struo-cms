using Microsoft.Extensions.Logging;
using SqlSugar;
using Struo.Application.Security;
using Struo.Infrastructure.Identity;
using Struo.Infrastructure.Localization;

namespace Struo.Infrastructure.Persistence;

/// <summary>
/// Unified initial-data seeding for every environment. A seeder fires ONLY when its target table was
/// created during this startup (present now, absent from <c>existingBefore</c>). Pre-existing tables —
/// even if manually emptied — are trusted and never re-seeded, preserving production data.
/// </summary>
public static class DataSeeder
{
    internal const string LanguagesTable = "languages";
    internal const string UsersTable = "users";
    internal const string RolesTable = "roles";
    internal const string DefaultAdminPassword = "admin";

    /// <summary>Lower-cased ordinal set of current DB table names. Reused for the before-snapshot.</summary>
    public static ISet<string> GetTableNames(ISqlSugarClient db) =>
        db.DbMaintenance.GetTableInfoList(false)
            .Select(t => t.Name.ToLowerInvariant())
            .ToHashSet(StringComparer.Ordinal);

    public static async Task SeedAsync(
        ISqlSugarClient db,
        ISet<string> existingBefore,
        IPasswordHasher hasher,
        string? bootstrapAdminEmail,
        string? bootstrapAdminPassword,
        IReadOnlyList<string> publicReadCollections,
        bool isProduction,
        ILogger logger,
        CancellationToken ct = default)
    {
        var existingAfter = GetTableNames(db);
        bool JustCreated(string table) =>
            existingAfter.Contains(table) && !existingBefore.Contains(table);

        if (JustCreated(LanguagesTable))
            await LanguageSeeder.SeedAsync(db);
        else
            logger.LogInformation("DataSeeder: skip LanguageSeeder — '{Table}' pre-existed.", LanguagesTable);

        if (JustCreated(UsersTable))
            await AdminUserSeeder.SeedAsync(db, hasher, bootstrapAdminEmail, bootstrapAdminPassword);
        else
            logger.LogInformation("DataSeeder: skip AdminUserSeeder — '{Table}' pre-existed.", UsersTable);

        if (JustCreated(RolesTable))
            await RbacSeeder.SeedAsync(db, bootstrapAdminEmail, publicReadCollections, ct);
        else
            logger.LogInformation("DataSeeder: skip RbacSeeder — '{Table}' pre-existed.", RolesTable);
    }
}
