using AwesomeAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using SqlSugar;
using Struo.Application.Configuration;
using Struo.Infrastructure.Metadata;
using Struo.Infrastructure.Migrations;
using Struo.Infrastructure.Migrations.Schema;
using Struo.Tests.Support;
using Xunit;

namespace Struo.Tests.Migrations.Core;

public sealed class CoreMigrationsLiveTests
{
    public static TheoryData<string> Backends => LiveBackend.Names();

    private static string NewPrefix() => "t" + Guid.NewGuid().ToString("N")[..8] + "_";

    private static Task<IReadOnlyList<MigrationInfo>> ApplyCore(StruoDbType dbType, string conn, string prefix) => new MigrationHost(
        new MigrationHostOptions(dbType, conn, prefix, [typeof(StruoMigration).Assembly], LockTimeoutSeconds: 30)
        { NamespaceFilter = "Struo.Infrastructure.Migrations.Core", Seed = CoreMigrationHarness.Seed() },
        NullLoggerFactory.Instance).ApplyAsync(default);

    private static void Drop(ISqlSugarClient db, string prefix)
    {
        foreach (var table in CoreSchemaParity.AllTableNames(db, prefix))
            if (db.DbMaintenance.IsAnyTable(table, false)) db.DbMaintenance.DropTable(table);
    }

    [Theory, MemberData(nameof(Backends))]
    public async Task The_core_migrations_satisfy_the_schema_checker(string backend)
    {
        var (_, dbType, connection) = LiveBackend.Get(backend);
        if (connection is not { } conn) return;
        LiveDatabases.GuardDisposable(conn);
        var prefix = NewPrefix();
        var db = CoreMigrationHarness.Client(dbType, conn, prefix);
        try
        {
            await ApplyCore(dbType, conn, prefix);

            var report = SchemaChecker.Check(db, dbType, FrameworkEntityTypes.All);
            report.HasErrors.Should().BeFalse(report.ToString());
        }
        finally { Drop(db, prefix); }
    }

    [Theory, MemberData(nameof(Backends))]
    public async Task The_core_migrations_build_the_schema_CodeFirst_builds(string backend)
    {
        var (_, dbType, connection) = LiveBackend.Get(backend);
        if (connection is not { } conn) return;
        LiveDatabases.GuardDisposable(conn);
        var codeFirstPrefix = NewPrefix();
        var migratedPrefix = NewPrefix();
        var codeFirst = CoreMigrationHarness.Client(dbType, conn, codeFirstPrefix);
        var migrated = CoreMigrationHarness.Client(dbType, conn, migratedPrefix);
        try
        {
            codeFirst.CodeFirst.InitTables(FrameworkEntityTypes.All.ToArray());
            await ApplyCore(dbType, conn, migratedPrefix);

            var diffs = CoreSchemaParity.Differences(codeFirst, migrated, dbType);
            diffs.Should().BeEmpty(Environment.NewLine + string.Join(Environment.NewLine, diffs));
        }
        finally
        {
            Drop(codeFirst, codeFirstPrefix);
            Drop(migrated, migratedPrefix);
        }
    }
}
