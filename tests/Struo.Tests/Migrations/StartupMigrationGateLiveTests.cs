using AwesomeAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using SqlSugar;
using Struo.Application.Configuration;
using Struo.Infrastructure.Identity;
using Struo.Infrastructure.Localization;
using Struo.Infrastructure.Metadata;
using Struo.Infrastructure.Migrations;
using Struo.Infrastructure.Persistence;
using Struo.Tests.Migrations.Core;
using Struo.Tests.Support;
using Xunit;

namespace Struo.Tests.Migrations;

public sealed class StartupMigrationGateLiveTests
{
    public static TheoryData<string> Backends => LiveBackend.Names("PostgreSQL", "SqlServer");

    private static async Task RunGate(StruoDbType dbType, string conn, string prefix, ListLogger logger)
    {
        var options = new DatabaseOptions
        {
            DbType = dbType, ConnectionString = conn, TablePrefix = prefix, MigrateOnStartup = true,
        };
        var db = SqlSugarClientFactory.Create(options, new TestCurrentUserAccessor(Guid.Empty));
        var host = new MigrationHost(
            new MigrationHostOptions(dbType, conn, prefix, [typeof(StruoMigration).Assembly], LockTimeoutSeconds: 60)
            { NamespaceFilter = "Struo.Infrastructure.Migrations.Core", Seed = CoreMigrationHarness.Seed() },
            NullLoggerFactory.Instance);

        await Task.Yield();
        await StartupMigrationGate.RunAsync(db, host, options, isDevelopment: true, FrameworkEntityTypes.All, logger, default);
    }

    [Theory, MemberData(nameof(Backends))]
    public async Task Two_gates_started_together_apply_each_core_migration_once_and_both_pass(string backend)
    {
        var (_, dbType, connection) = LiveBackend.Get(backend);
        if (connection is not { } conn) return;
        LiveDatabases.GuardDisposable(conn);
        var prefix = "t" + Guid.NewGuid().ToString("N")[..8] + "_";
        var (first, second) = (new ListLogger(), new ListLogger());
        var db = SqlSugarClientFactory.Create(
            new DatabaseOptions { DbType = dbType, ConnectionString = conn, TablePrefix = prefix },
            new TestCurrentUserAccessor(Guid.Empty));
        try
        {
            await Task.WhenAll(RunGate(dbType, conn, prefix, first), RunGate(dbType, conn, prefix, second));

            var applied = first.Entries.Concat(second.Entries).Count(e => e.Message.StartsWith("Applied migration"));
            applied.Should().Be(2);
            var host = new MigrationHost(
                new MigrationHostOptions(dbType, conn, prefix, [typeof(StruoMigration).Assembly], 60)
                { NamespaceFilter = "Struo.Infrastructure.Migrations.Core" },
                NullLoggerFactory.Instance);
            host.GetStatus().Should().HaveCount(2).And.OnlyContain(m => m.State == MigrationState.Applied);
            db.Queryable<Language>().Count().Should().Be(2);
            db.Queryable<User>().Count().Should().Be(1);
        }
        finally
        {
            foreach (var table in CoreSchemaParity.AllTableNames(db, prefix))
                if (db.DbMaintenance.IsAnyTable(table, false)) db.DbMaintenance.DropTable(table);
        }
    }
}
