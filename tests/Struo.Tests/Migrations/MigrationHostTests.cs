using AwesomeAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using SqlSugar;
using Struo.Application.Configuration;
using Struo.Infrastructure.Migrations;
using Struo.Tests.Support;
using Xunit;

namespace Struo.Tests.Migrations;

public sealed class MigrationHostTests : IDisposable
{
    private const string Probes = "Struo.Tests.Migrations.Probes";
    private readonly SqliteTestDatabase _file = new();

    public void Dispose() => _file.Dispose();

    private MigrationHost Host(string ns, string prefix = "struo_") => new(
        new MigrationHostOptions(StruoDbType.Sqlite, _file.ConnectionString, prefix,
            [typeof(MigrationHostTests).Assembly], LockTimeoutSeconds: 5) { NamespaceFilter = $"{Probes}.{ns}" },
        NullLoggerFactory.Instance);

    private string[] Tables() => new SqlSugarClient(new ConnectionConfig
        { DbType = SqlSugar.DbType.Sqlite, ConnectionString = _file.ConnectionString, IsAutoCloseConnection = true })
        .DbMaintenance.GetTableInfoList(false).Select(t => t.Name.ToLowerInvariant()).ToArray();

    [Fact]
    public async Task Apply_runs_pending_migrations_in_version_order_and_records_them()
    {
        var applied = await Host("Upgrade.V1").ApplyAsync(default);

        applied.Select(m => m.Version).Should().Equal(1, 3);
        Tables().Should().Contain(["probe_alpha", "probe_gamma", "struo_schema_versions"]);
        (await Host("Upgrade.V1").ApplyAsync(default)).Should().BeEmpty();
    }

    [Fact]
    public async Task Apply_applies_an_older_version_shipped_after_a_newer_one()
    {
        await Host("Upgrade.V1").ApplyAsync(default);

        var applied = await Host("Upgrade").ApplyAsync(default);

        applied.Select(m => m.Version).Should().Equal(2);
        Tables().Should().Contain("probe_beta");
    }

    [Fact]
    public async Task Version_table_carries_the_configured_prefix()
    {
        await Host("Upgrade.V1", prefix: "acme_").ApplyAsync(default);

        Tables().Should().Contain("acme_schema_versions").And.NotContain("struo_schema_versions");
    }

    [Fact]
    public async Task Failing_migration_stops_the_run_and_is_not_recorded()
    {
        var act = () => Host("Failing").ApplyAsync(default);

        await act.Should().ThrowAsync<MigrationFailedException>().WithMessage("*11*");
        var status = Host("Failing").GetStatus();
        status.Single(m => m.Version == 10).State.Should().Be(MigrationState.Applied);
        status.Single(m => m.Version == 11).State.Should().Be(MigrationState.Pending);
        status.Single(m => m.Version == 12).State.Should().Be(MigrationState.Pending);
        Tables().Should().NotContain("probe_fail_c");
    }

    [Fact]
    public async Task Duplicate_versions_fail_before_anything_runs()
    {
        var act = () => Host("Duplicate").ApplyAsync(default);

        await act.Should().ThrowAsync<MigrationFailedException>().WithMessage("*20*");
        Tables().Should().NotContain(["probe_dup_one", "probe_dup_two"]);
    }

    [Fact]
    public async Task Status_reports_applied_pending_and_orphaned()
    {
        await Host("Upgrade").ApplyAsync(default);

        var status = Host("Upgrade.V1").GetStatus();

        status.Select(m => (m.Version, m.State)).Should().Equal(
            (1L, MigrationState.Applied), (2L, MigrationState.Orphaned), (3L, MigrationState.Applied));
        status.Where(m => m.State == MigrationState.Applied).Should().OnlyContain(m => m.AppliedOn != null);
    }

    [Fact]
    public async Task StruoMigration_exposes_the_framework_prefix()
    {
        await Host("Context", prefix: "acme_").ApplyAsync(default);

        Tables().Should().Contain("acme_ctxprobe");
    }
}
