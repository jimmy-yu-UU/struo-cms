using AwesomeAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Struo.Application.Configuration;
using Struo.Infrastructure.Metadata;
using Struo.Infrastructure.Migrations;
using Struo.Infrastructure.Persistence;
using Struo.Tests.Migrations.Core;
using Struo.Tests.Support;
using Xunit;

namespace Struo.Tests.Migrations;

/// <summary>
/// Two hosts in one process, on different providers and the same table prefix, each migrate a fresh
/// database and pass the Development schema check.
/// </summary>
public sealed class CrossProviderStartupGateLiveTests
{
    public static TheoryData<TestBackendKind, TestBackendKind> Pairs => new()
    {
        { TestBackendKind.Sqlite, TestBackendKind.SqlServer },
        { TestBackendKind.SqlServer, TestBackendKind.Sqlite },
        { TestBackendKind.PostgreSQL, TestBackendKind.SqlServer },
        { TestBackendKind.SqlServer, TestBackendKind.PostgreSQL },
        { TestBackendKind.MySql, TestBackendKind.PostgreSQL },
        { TestBackendKind.PostgreSQL, TestBackendKind.MySql },
    };

    [Theory, MemberData(nameof(Pairs))]
    public async Task Both_hosts_migrate_and_pass_the_schema_check(TestBackendKind first, TestBackendKind second)
    {
        if (!Configured(first) || !Configured(second)) return;
        var prefix = "g" + Guid.NewGuid().ToString("N")[..8] + "_";

        using var firstDb = Create(first);
        using var secondDb = Create(second);

        await RunGate(firstDb, prefix);
        await RunGate(secondDb, prefix);

        var db = SqlSugarClientFactory.Create(
            new DatabaseOptions { DbType = secondDb.DbType, ConnectionString = secondDb.ConnectionString, TablePrefix = prefix },
            new TestCurrentUserAccessor(Guid.Empty));
        db.Queryable<Struo.Infrastructure.Localization.Language>().Count().Should().Be(2);
    }

    private static bool Configured(TestBackendKind kind) =>
        kind == TestBackendKind.Sqlite || !string.IsNullOrWhiteSpace(LiveBackend.Get(kind.ToString()).Connection);

    private static ITestDatabase Create(TestBackendKind kind) =>
        kind == TestBackendKind.Sqlite ? TestBackend.CreateSqliteDatabase() : TestBackend.CreateLiveDatabase(kind, "xgate");

    private static Task RunGate(ITestDatabase database, string prefix)
    {
        var options = new DatabaseOptions
        {
            DbType = database.DbType, ConnectionString = database.ConnectionString, TablePrefix = prefix, MigrateOnStartup = true,
        };
        var db = SqlSugarClientFactory.Create(options, new TestCurrentUserAccessor(Guid.Empty));
        var host = new MigrationHost(
            new MigrationHostOptions(database.DbType, database.ConnectionString, prefix, [typeof(StruoMigration).Assembly], LockTimeoutSeconds: 60)
            { NamespaceFilter = "Struo.Infrastructure.Migrations.Core", Seed = CoreMigrationHarness.Seed() },
            NullLoggerFactory.Instance);
        return StartupMigrationGate.RunAsync(
            db, host, options, isDevelopment: true, FrameworkEntityTypes.All, NullLogger.Instance, default);
    }
}
