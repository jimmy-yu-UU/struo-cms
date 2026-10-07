using AwesomeAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SqlSugar;
using Struo.Application.Configuration;
using Struo.Infrastructure.Metadata;
using Struo.Infrastructure.Migrations;
using Struo.Infrastructure.Migrations.Core;
using Struo.Tests.Migrations.Core;
using Struo.Tests.Support;
using Xunit;

namespace Struo.Tests.Migrations;

public sealed class StartupMigrationGateTests : IDisposable
{
    private readonly CoreMigrationHarness _harness = new();

    public void Dispose() => _harness.Dispose();

    private DatabaseOptions Options(bool migrateOnStartup) => new()
    {
        DbType = StruoDbType.Sqlite, TablePrefix = _harness.Prefix, MigrateOnStartup = migrateOnStartup,
    };

    private Task Run(bool migrateOnStartup, bool isDevelopment, ILogger? logger = null, bool withSeed = true) =>
        StartupMigrationGate.RunAsync(
            _harness.Db(), _harness.Host(withSeed ? CoreMigrationHarness.Seed() : null), Options(migrateOnStartup),
            isDevelopment, FrameworkEntityTypes.All, logger ?? NullLogger.Instance, default);

    private static string[] TableNames(ISqlSugarClient db) =>
        db.DbMaintenance.GetTableInfoList(false).Select(t => t.Name).Order().ToArray();

    private string VersionTable => StruoVersionTableMetaData.TableNameFor(_harness.Prefix);

    [Fact]
    public async Task A_database_with_applied_migrations_passes()
    {
        await _harness.Host(CoreMigrationHarness.Seed()).ApplyAsync(default);

        var act = () => Run(migrateOnStartup: false, isDevelopment: true);

        await act.Should().NotThrowAsync();
        _harness.Host(null).GetStatus().Should().OnlyContain(m => m.State == MigrationState.Applied);
    }

    [Fact]
    public async Task An_empty_database_fails_with_the_migrate_hint_and_creates_nothing()
    {
        var act = () => Run(migrateOnStartup: false, isDevelopment: false);

        var ex = (await act.Should().ThrowAsync<InvalidOperationException>()).Which;
        ex.Message.Should().Contain("'migrate'").And.NotContain("\n");
        TableNames(_harness.Db()).Should().BeEmpty();
    }

    [Fact]
    public async Task Pending_migrations_fail_with_the_migrate_hint()
    {
        await _harness.Host(CoreMigrationHarness.Seed()).ApplyAsync(default);
        var db = _harness.Db();
        db.Deleteable<SchemaVersionRow>().AS(VersionTable).Where(r => r.Version == SeedCoreData.Version).ExecuteCommand();

        var act = () => Run(migrateOnStartup: false, isDevelopment: false);

        var ex = (await act.Should().ThrowAsync<InvalidOperationException>()).Which;
        ex.Message.Should().Contain("'migrate'").And.Contain(SeedCoreData.Version.ToString()).And.NotContain("\n");
    }

    [Fact]
    public async Task A_database_with_core_tables_and_no_history_fails_with_the_baseline_hint_and_creates_nothing()
    {
        var db = _harness.Db();
        db.CodeFirst.InitTables(FrameworkEntityTypes.All.ToArray());
        var before = TableNames(db);

        var act = () => Run(migrateOnStartup: false, isDevelopment: true);

        var ex = (await act.Should().ThrowAsync<InvalidOperationException>()).Which;
        ex.Message.Should().Contain("'migrate:baseline'").And.NotContain("\n");
        TableNames(db).Should().Equal(before);
    }

    [Fact]
    public async Task MigrateOnStartup_on_a_database_with_core_tables_and_no_history_fails_with_the_baseline_hint_and_changes_nothing()
    {
        var db = _harness.Db();
        db.CodeFirst.InitTables(FrameworkEntityTypes.All.ToArray());
        var before = TableNames(db);

        var act = () => Run(migrateOnStartup: true, isDevelopment: false);

        var ex = (await act.Should().ThrowAsync<MigrationBaselineRefusedException>()).Which;
        ex.Message.Should().Contain("'migrate:baseline'").And.NotContain("\n");
        TableNames(db).Should().Equal(before);
    }

    [Fact]
    public async Task MigrateOnStartup_applies_the_pending_migrations_then_passes()
    {
        await Run(migrateOnStartup: true, isDevelopment: true);

        _harness.Host(null).GetStatus().Should().OnlyContain(m => m.State == MigrationState.Applied);
        _harness.Db().Queryable<Struo.Infrastructure.Identity.User>().Count().Should().Be(1);
    }

    [Fact]
    public async Task MigrateOnStartup_on_an_up_to_date_database_changes_nothing()
    {
        await Run(migrateOnStartup: true, isDevelopment: true);
        var before = TableNames(_harness.Db());

        await Run(migrateOnStartup: true, isDevelopment: true);

        TableNames(_harness.Db()).Should().Equal(before);
    }

    [Fact]
    public async Task A_failing_migration_aborts_startup_with_one_line()
    {
        var act = () => Run(migrateOnStartup: true, isDevelopment: false, withSeed: false);

        var ex = (await act.Should().ThrowAsync<MigrationFailedException>()).Which;
        ex.Message.Should().NotContain("\n");
    }

    private async Task<ISqlSugarClient> DriftedDatabase()
    {
        await _harness.Host(CoreMigrationHarness.Seed()).ApplyAsync(default);
        var db = _harness.Db();
        db.DbMaintenance.DropTable(_harness.Prefix + "permissions");
        return db;
    }

    [Fact]
    public async Task Development_with_a_drifted_schema_fails_with_the_report()
    {
        await DriftedDatabase();

        var act = () => Run(migrateOnStartup: false, isDevelopment: true);

        (await act.Should().ThrowAsync<InvalidOperationException>()).Which.Message
            .Should().Contain(_harness.Prefix + "permissions").And.Contain("table does not exist");
    }

    [Fact]
    public async Task Production_does_not_check_the_schema()
    {
        var db = await DriftedDatabase();

        var act = () => Run(migrateOnStartup: false, isDevelopment: false);

        await act.Should().NotThrowAsync();
        db.DbMaintenance.IsAnyTable(_harness.Prefix + "permissions", false).Should().BeFalse();
    }
}
