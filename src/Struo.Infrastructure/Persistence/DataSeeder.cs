using Microsoft.Extensions.Logging;
using SqlSugar;
using Struo.Application.Configuration;
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
    internal const string DefaultAdminPassword = "admin";

    /// <summary>Lower-cased ordinal set of current DB table names. Reused for the before-snapshot.</summary>
    public static ISet<string> GetTableNames(ISqlSugarClient db) =>
        db.DbMaintenance.GetTableInfoList(false)
            .Select(t => t.Name.ToLowerInvariant())
            .ToHashSet(StringComparer.Ordinal);

    public static async Task SeedAsync(
        ISqlSugarClient db,
        ISet<string> existingBefore,
        LocalizationOptions localization,
        IPasswordHasher hasher,
        string? bootstrapAdminEmail,
        string? bootstrapAdminPassword,
        IReadOnlyList<string> publicReadCollections,
        bool isProduction,
        ILogger logger,
        CancellationToken ct = default)
    {
        var existingAfter = GetTableNames(db);
        var languagesTable = db.EntityMaintenance.GetTableName<Language>().ToLowerInvariant();
        var usersTable = db.EntityMaintenance.GetTableName<User>().ToLowerInvariant();
        var rolesTable = db.EntityMaintenance.GetTableName<Role>().ToLowerInvariant();
        bool JustCreated(string table) =>
            existingAfter.Contains(table) && !existingBefore.Contains(table);

        if (JustCreated(languagesTable))
            await LanguageSeeder.SeedAsync(db, localization);
        else
            logger.LogInformation("DataSeeder: skip LanguageSeeder — '{Table}' pre-existed.", languagesTable);

        if (JustCreated(usersTable))
            await AdminUserSeeder.SeedAsync(db, hasher, bootstrapAdminEmail, bootstrapAdminPassword);
        else
            logger.LogInformation("DataSeeder: skip AdminUserSeeder — '{Table}' pre-existed.", usersTable);

        if (JustCreated(rolesTable))
            await RbacSeeder.SeedAsync(db, bootstrapAdminEmail, publicReadCollections, ct);
        else
            logger.LogInformation("DataSeeder: skip RbacSeeder — '{Table}' pre-existed.", rolesTable);

        WarnIfDefaultAdminPasswordInProduction(isProduction, bootstrapAdminPassword, logger);
    }

    internal static void WarnIfDefaultAdminPasswordInProduction(
        bool isProduction, string? password, ILogger logger)
    {
        if (isProduction && string.Equals(password, DefaultAdminPassword, StringComparison.Ordinal))
            logger.LogWarning(
                "Bootstrap admin is using the default password '{Default}'. Change it immediately via Auth__BootstrapAdmin__Password.",
                DefaultAdminPassword);
    }
}
