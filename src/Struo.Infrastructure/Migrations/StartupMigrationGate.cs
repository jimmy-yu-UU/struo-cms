using Microsoft.Extensions.Logging;
using SqlSugar;
using Struo.Application.Configuration;
using Struo.Infrastructure.Migrations.Schema;

namespace Struo.Infrastructure.Migrations;

/// <summary>
/// The startup schema step: applies migrations when <c>Database:MigrateOnStartup</c> is set, otherwise
/// refuses to start on a database that is not fully migrated. In Development it also checks the schema
/// against the entity model. It creates nothing when it fails.
/// </summary>
public static class StartupMigrationGate
{
    public static async Task RunAsync(
        ISqlSugarClient db, MigrationHost host, DatabaseOptions options, bool isDevelopment,
        IReadOnlyList<Type> entityTypes, ILogger logger, CancellationToken ct)
    {
        if (options.MigrateOnStartup)
            await ApplyAsync(host, logger, ct);
        else
            RequireMigrated(db, host, options.TablePrefix);

        if (isDevelopment)
            CheckSchema(db, options.DbType, entityTypes);
    }

    private static async Task ApplyAsync(MigrationHost host, ILogger logger, CancellationToken ct)
    {
        var appliedMigrations = await host.ApplyAsync(ct);
        if (!logger.IsEnabled(LogLevel.Information)) return;
        foreach (var applied in appliedMigrations)
            logger.LogInformation("Applied migration {Version} {Description}.", applied.Version, applied.Description);
    }

    private static void RequireMigrated(ISqlSugarClient db, MigrationHost host, string tablePrefix)
    {
        var versionTable = StruoVersionTableMetaData.TableNameFor(tablePrefix);
        if (!db.DbMaintenance.IsAnyTable(versionTable, false) && CoreTables.Exist(db, tablePrefix))
            throw new InvalidOperationException(
                "The database has the framework tables but no migration history; run the 'migrate:baseline' command once, then start the application.");

        var pending = host.GetStatus().Where(m => m.State == MigrationState.Pending).ToList();
        if (pending.Count > 0)
            throw new InvalidOperationException(
                $"{pending.Count} migration(s) pending, the lowest is {pending[0].Version}; run the 'migrate' command, or set Database:MigrateOnStartup to true.");
    }

    private static void CheckSchema(ISqlSugarClient db, StruoDbType dbType, IReadOnlyList<Type> entityTypes)
    {
        var report = SchemaChecker.Check(db, dbType, entityTypes);
        if (report.HasErrors)
            throw new InvalidOperationException(
                "The database schema differs from the entity model; run the 'migrate:check' command for details." +
                Environment.NewLine + report);
    }
}
