using AwesomeAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using SqlSugar;
using Struo.Application.Configuration;
using Struo.Infrastructure.Files;
using Struo.Infrastructure.Metadata;
using Struo.Infrastructure.Migrations;
using Struo.Infrastructure.Migrations.Schema;
using Struo.Infrastructure.Persistence;
using Struo.Tests.Support;
using Xunit;

namespace Struo.Tests.Migrations.Core;

public sealed class CoreMigrationsLiveTests
{
    public static TheoryData<string> Backends => LiveBackend.Names();

    private static string NewPrefix() => "t" + Guid.NewGuid().ToString("N")[..8] + "_";

    private static ISqlSugarClient Client(StruoDbType dbType, string conn, string prefix)
    {
        var policy = new TranslationSidecarIndexPolicy(new Dictionary<Type, TranslationSidecarKey>
        {
            [typeof(FileTranslation)] = TranslationSidecarIndexPolicy.KeyFor(typeof(FileTranslation), "FileId", "Locale")
        });
        return SqlSugarClientFactory.Create(
            new DatabaseOptions { DbType = dbType, ConnectionString = conn, TablePrefix = prefix },
            new TestCurrentUserAccessor(Guid.Empty), policy);
    }

    private static Task ApplyCore(StruoDbType dbType, string conn, string prefix) => new MigrationHost(
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
        var db = Client(dbType, conn, prefix);
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
        var codeFirst = Client(dbType, conn, codeFirstPrefix);
        var migrated = Client(dbType, conn, migratedPrefix);
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
