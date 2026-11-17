using AwesomeAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using SqlSugar;
using Struo.Application.Configuration;
using Struo.Infrastructure.Migrations;
using Struo.Tests.Migrations.Core;
using Xunit;

namespace Struo.Tests.Migrations.Utf8;

/// <summary>The apply path asks for the database-defaults check after it has found pending migrations and before the runner executes anything.</summary>
public sealed class MigrationDefaultsRefusalTests
{
    private const string Reason = "refused for a reason";

    private static MigrationHost Host(CoreMigrationHarness harness, Func<ISqlSugarClient, string?> refusal) =>
        new(harness.HostOptions(CoreMigrationHarness.Seed()) with { DefaultsRefusal = refusal }, NullLoggerFactory.Instance);

    [Fact]
    public async Task A_refusal_stops_the_run_with_its_message_before_any_table_or_version_row_exists()
    {
        using var harness = new CoreMigrationHarness();

        var failure = await Assert.ThrowsAsync<MigrationFailedException>(() => Host(harness, _ => Reason).ApplyAsync(default));

        failure.Message.Should().Be(Reason);
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
    public async Task Nothing_pending_means_the_check_is_not_asked()
    {
        using var harness = new CoreMigrationHarness();
        await Host(harness, _ => null).ApplyAsync(default);

        var applied = await Host(harness, _ => throw new InvalidOperationException("must not run")).ApplyAsync(default);

        applied.Should().BeEmpty();
    }

    [Fact]
    public async Task The_default_check_applies_the_migrations_on_a_backend_other_than_MySql()
    {
        using var harness = new CoreMigrationHarness();
        if (harness.Database.DbType == StruoDbType.MySql) return;

        var applied = await harness.Host(CoreMigrationHarness.Seed()).ApplyAsync(default);

        applied.Should().NotBeEmpty();
    }
}
