using AwesomeAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using SqlSugar;
using Struo.Infrastructure.Migrations;
using Struo.Infrastructure.Migrations.Schema;
using Struo.Tests.Migrations.Core;
using Struo.Tests.Support;
using Xunit;

namespace Struo.Tests.Migrations.Utf8;

/// <summary>The apply path asks for the database-defaults check after it has found pending migrations and before the runner executes anything.</summary>
public sealed class MigrationDefaultsRefusalTests
{
    private static MigrationHost Host(CoreMigrationHarness harness, Func<ISqlSugarClient, CatalogDefaults?> catalog) =>
        new(harness.HostOptions(CoreMigrationHarness.Seed()) with { DefaultsCatalog = catalog }, NullLoggerFactory.Instance);

    private static CatalogDefaults Latin1Database(CoreMigrationHarness harness) =>
        new("scratch",
            [new SchemataRow { SchemaName = "scratch", DefaultCharacterSetName = "latin1", DefaultCollationName = "latin1_swedish_ci" }],
            [new TablesRow { TableSchema = "scratch", TableName = harness.Prefix + "users", TableCollation = "latin1_swedish_ci" }]);

    [Fact]
    public async Task A_refusal_stops_the_run_with_its_message_before_any_table_or_version_row_exists()
    {
        using var harness = new CoreMigrationHarness();

        var failure = await Assert.ThrowsAsync<MigrationFailedException>(
            () => Host(harness, _ => Latin1Database(harness)).ApplyAsync(default));

        failure.Message.Should().Contain("Database `scratch` has the default character set latin1.")
            .And.Contain($"ALTER TABLE `{harness.Prefix}users` CONVERT TO CHARACTER SET utf8mb4;");
        var db = harness.Db();
        db.DbMaintenance.IsAnyTable(StruoVersionTableMetaData.TableNameFor(harness.Prefix), false).Should().BeFalse();
        CoreTables.Exist(db, harness.Prefix).Should().BeFalse();
    }

    [Fact]
    public async Task The_check_runs_before_the_runner_creates_the_first_table()
    {
        using var harness = new CoreMigrationHarness();
        var seen = new List<string>();

        await Host(harness, db =>
        {
            seen.Add($"version={db.DbMaintenance.IsAnyTable(StruoVersionTableMetaData.TableNameFor(harness.Prefix), false)}");
            seen.Add($"users={CoreTables.Exist(db, harness.Prefix)}");
            return null;
        }).ApplyAsync(default);

        seen.Should().Equal("version=False", "users=False");
        CoreTables.Exist(harness.Db(), harness.Prefix).Should().BeTrue();
    }

    [Fact]
    public async Task A_check_that_cannot_read_the_database_fails_the_run_as_a_migration_failure()
    {
        using var harness = new CoreMigrationHarness();

        var failure = await Assert.ThrowsAsync<MigrationFailedException>(
            () => Host(harness, _ => throw new InvalidOperationException("catalog unreadable")).ApplyAsync(default));

        failure.Message.Should().Be("Could not inspect the database: catalog unreadable");
        CoreTables.Exist(harness.Db(), harness.Prefix).Should().BeFalse();
    }

    [Fact]
    public async Task A_fault_in_the_decision_is_not_reported_as_an_unreadable_database()
    {
        using var harness = new CoreMigrationHarness();
        var broken = new CatalogDefaults("scratch", null!, []);

        var failure = await Record.ExceptionAsync(() => Host(harness, _ => broken).ApplyAsync(default));

        failure.Should().NotBeNull().And.NotBeOfType<MigrationFailedException>();
        CoreTables.Exist(harness.Db(), harness.Prefix).Should().BeFalse();
    }

    [Fact]
    public async Task Nothing_pending_means_the_check_is_not_asked()
    {
        using var harness = new CoreMigrationHarness();
        await Host(harness, _ => null).ApplyAsync(default);

        var applied = await Host(harness, _ => throw new InvalidOperationException("must not run")).ApplyAsync(default);

        applied.Should().BeEmpty();
    }

    [NotOnBackendFact("the default check reads the catalog of a live MySQL or MariaDB database",
        TestBackendKind.MySql, TestBackendKind.MariaDb)]
    public async Task The_default_check_applies_the_migrations_on_a_backend_other_than_MySql()
    {
        using var harness = new CoreMigrationHarness();

        var applied = await harness.Host(CoreMigrationHarness.Seed()).ApplyAsync(default);

        applied.Should().NotBeEmpty();
    }
}
