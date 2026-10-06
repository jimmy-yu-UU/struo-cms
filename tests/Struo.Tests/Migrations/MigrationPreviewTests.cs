using AwesomeAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using SqlSugar;
using Struo.Application.Configuration;
using Struo.Infrastructure.Migrations;
using Struo.Tests.Support;
using Xunit;

namespace Struo.Tests.Migrations;

public sealed class MigrationPreviewTests : IDisposable
{
    private readonly SqliteTestDatabase _file = new();
    public void Dispose() => _file.Dispose();

    private MigrationHost Host(string ns) => new(
        new MigrationHostOptions(StruoDbType.Sqlite, _file.ConnectionString, "struo_",
            [typeof(MigrationPreviewTests).Assembly], 5)
        { NamespaceFilter = "Struo.Tests.Migrations.Probes." + ns },
        NullLoggerFactory.Instance);

    private string[] Tables() => new SqlSugarClient(new ConnectionConfig
        { DbType = DbType.Sqlite, ConnectionString = _file.ConnectionString, IsAutoCloseConnection = true })
        .DbMaintenance.GetTableInfoList(false).Select(t => t.Name.ToLowerInvariant()).ToArray();

    [Fact]
    public void Preview_prints_sql_and_writes_nothing()
    {
        var sql = new StringWriter();

        var pending = Host("Upgrade.V1").Preview(sql);

        pending.Select(m => m.Version).Should().Equal(1, 3);
        sql.ToString().Should().Contain("CREATE TABLE").And.Contain("probe_alpha").And.Contain("probe_gamma");
        Tables().Should().BeEmpty();
    }

    [Fact]
    public async Task Preview_after_partial_apply_lists_only_pending()
    {
        await Host("Upgrade.V1").ApplyAsync(default);
        var sql = new StringWriter();

        var pending = Host("Upgrade").Preview(sql);

        pending.Select(m => m.Version).Should().Equal(2);
        sql.ToString().Should().Contain("probe_beta").And.NotContain("probe_alpha");
    }

    [Fact]
    public void Status_on_a_never_migrated_database_creates_no_table()
    {
        var status = Host("Upgrade.V1").GetStatus();

        status.Should().OnlyContain(m => m.State == MigrationState.Pending);
        Tables().Should().BeEmpty();
    }
}
