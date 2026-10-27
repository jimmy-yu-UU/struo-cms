// src/Struo.Api/Hosting/StruoCmsStartupChecks.cs
using Microsoft.Extensions.Options;
using SqlSugar;
using Struo.Infrastructure.Migrations;
using Struo.Infrastructure.Persistence;

namespace Struo.Api.Hosting;

/// <summary>The configuration warnings, migration gate and database checks run by <c>UseStruoCmsAsync</c> before the host starts serving.</summary>
internal static class StruoCmsStartupChecks
{
    private const string StartupLoggerCategory = "Struo.Startup";

    internal static async Task RunStruoStartupChecksAsync(this WebApplication app)
    {
        using var scope = app.Services.CreateScope();
        var sp = scope.ServiceProvider;
        // Needs only configuration and DI, so it runs before anything touches the database.
        VerifyCustomFileStorage(sp);
        var startupLogger = sp.GetRequiredService<ILoggerFactory>().CreateLogger(StartupLoggerCategory);
        var dbOptions = sp.GetRequiredService<IOptions<Struo.Application.Configuration.DatabaseOptions>>().Value;
        RemovedDatabaseKeyWarnings.Log(
            app.Configuration, startupLogger);
        Struo.Api.Auth.OidcLegacyKeyWarnings.Log(
            app.Configuration, startupLogger);
        Struo.Api.Auth.AuthWiring.WarnIfSessionsAreInMemory(
            app.Configuration, app.Environment, startupLogger);
        var host = new MigrationHost(
            MigrationHostOptions.FromServices(sp, includeSeed: dbOptions.MigrateOnStartup),
            sp.GetRequiredService<ILoggerFactory>());

        await StartupMigrationGate.RunAsync(
            sp.GetRequiredService<ISqlSugarClient>(),
            host,
            dbOptions,
            app.Environment.IsDevelopment(),
            sp.GetRequiredService<Struo.Application.Metadata.IEntityTypeCollector>().CollectForInitTables(),
            sp.GetRequiredService<ILoggerFactory>().CreateLogger("Struo.Migrations"),
            CancellationToken.None);

        await Struo.Infrastructure.Persistence.SqlServerIsolationCheck.WarnIfSnapshotIsolationIsOffAsync(
            sp.GetRequiredService<ISqlSugarClient>(), dbOptions.DbType,
            startupLogger);

        await StruoCmsGraphQlCheck.RunAsync(sp);
    }

    // Backend=custom has no core-built storage: resolving it here makes a missing registration fail
    // startup with the message naming the key, not the first file request.
    private static void VerifyCustomFileStorage(IServiceProvider sp)
    {
        var files = sp.GetRequiredService<Struo.Application.Files.FileStorageOptions>();
        if (string.Equals(files.Backend, "custom", StringComparison.OrdinalIgnoreCase))
            _ = sp.GetRequiredService<Struo.Application.Files.IFileStorage>();
    }
}
