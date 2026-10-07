using AwesomeAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using SqlSugar;
using Struo.Application.Configuration;
using Struo.Infrastructure.Migrations;
using Struo.Infrastructure.Persistence;
using Struo.Tests.Support;
using Xunit;

namespace Struo.Tests.Migrations;

public sealed class StruoMigrationTypeTests
{
    public static TheoryData<string> LiveBackends => LiveBackend.Names();

    private static MigrationHost Host(StruoDbType db, string conn, string prefix, string ns) => new(
        new MigrationHostOptions(db, conn, prefix, [typeof(StruoMigrationTypeTests).Assembly], 30)
        { NamespaceFilter = "Struo.Tests.Migrations.Probes." + ns },
        NullLoggerFactory.Instance);

    private static Dictionary<string, string> ColumnTypes(StruoDbType db, string conn, string table) =>
        new SqlSugarClient(new ConnectionConfig
            { DbType = DbTypeMapper.Map(db), ConnectionString = conn, IsAutoCloseConnection = true })
        .DbMaintenance.GetColumnInfosByTableName(table, false)
        .ToDictionary(c => c.DbColumnName.ToLowerInvariant(), c => c.DataType.ToLowerInvariant());

    [Fact]
    public async Task Shapes_resolve_to_the_sqlite_literals()
    {
        using var file = new SqliteTestDatabase();
        await Host(StruoDbType.Sqlite, file.ConnectionString, "struo_", "Types").ApplyAsync(default);

        var types = ColumnTypes(StruoDbType.Sqlite, file.ConnectionString, "struo_typeprobe");
        types["at"].Should().Be("timestamptz");
        types["body"].Should().Be("text");
        types["data"].Should().Be("text");
    }

    private static readonly Dictionary<string, (string Tz, string LongText)> LiveLiterals = new()
    {
        ["PostgreSQL"] = ("timestamptz", "text"),
        ["SqlServer"] = ("datetimeoffset", "nvarchar"),
        ["MySql"] = ("datetime", "longtext"),
        ["MariaDb"] = ("datetime", "longtext"),
    };

    [Theory, MemberData(nameof(LiveBackends))]
    public async Task Shapes_resolve_to_the_live_backend_literals(string backend)
    {
        var (_, db, connection) = LiveBackend.Get(backend);
        var (tz, longText) = LiveLiterals[backend];
        if (connection is not { } conn) return;
        LiveDatabases.GuardDisposable(conn);
        var prefix = "t" + Guid.NewGuid().ToString("N")[..8] + "_";
        var client = new SqlSugarClient(new ConnectionConfig
            { DbType = DbTypeMapper.Map(db), ConnectionString = conn, IsAutoCloseConnection = true });
        try { client.DbMaintenance.CreateDatabase(); } catch { /* exists */ }
        try
        {
            await Host(db, conn, prefix, "Types").ApplyAsync(default);

            var types = ColumnTypes(db, conn, prefix + "typeprobe");
            types["at"].Should().Be(tz);
            types["body"].Should().Be(longText);
            types["data"].Should().Be(longText);
        }
        finally
        {
            foreach (var t in new[] { prefix + "typeprobe", prefix + "schema_versions" })
                if (client.DbMaintenance.IsAnyTable(t, false)) client.DbMaintenance.DropTable(t);
        }
    }

    [Fact]
    public async Task Translation_unique_index_uses_the_policy_name()
    {
        using var file = new SqliteTestDatabase();
        await Host(StruoDbType.Sqlite, file.ConnectionString, "struo_", "Sidecar").ApplyAsync(default);

        var client = new SqlSugarClient(new ConnectionConfig
            { DbType = DbType.Sqlite, ConnectionString = file.ConnectionString, IsAutoCloseConnection = true });
        var ddl = client.Ado.SqlQuerySingle<string>(
            "SELECT sql FROM sqlite_master WHERE type = 'index' AND name = 'ux_probe_translations_fk_locale'");
        ddl.Should().ContainEquivalentOf("unique").And.Contain("probeid").And.Contain("locale");
    }
}
