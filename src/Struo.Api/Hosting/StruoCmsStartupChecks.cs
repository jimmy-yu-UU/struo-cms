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
        var dbOptions = sp.GetRequiredService<IOptions<Struo.Application.Configuration.DatabaseOptions>>().Value;
        RemovedDatabaseKeyWarnings.Log(
            app.Configuration, sp.GetRequiredService<ILoggerFactory>().CreateLogger(StartupLoggerCategory));
        Struo.Api.Auth.OidcLegacyKeyWarnings.Log(
            app.Configuration, sp.GetRequiredService<ILoggerFactory>().CreateLogger(StartupLoggerCategory));
        Struo.Api.Auth.AuthWiring.WarnIfSessionsAreInMemory(
            app.Configuration, app.Environment, sp.GetRequiredService<ILoggerFactory>().CreateLogger(StartupLoggerCategory));
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
            sp.GetRequiredService<ILoggerFactory>().CreateLogger(StartupLoggerCategory));
    }
}
