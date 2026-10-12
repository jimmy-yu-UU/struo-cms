using AwesomeAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using SqlSugar;
using Struo.Application.Configuration;
using Struo.Infrastructure.Migrations;
using Struo.Infrastructure.Migrations.Schema;
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

    private static Dictionary<string, DbColumnInfo> Columns(StruoDbType db, string conn, string table) =>
        new SqlSugarClient(new ConnectionConfig
            { DbType = DbTypeMapper.Map(db), ConnectionString = conn, IsAutoCloseConnection = true })
        .DbMaintenance.GetColumnInfosByTableName(table, false)
        .ToDictionary(c => c.DbColumnName.ToLowerInvariant());

    private static Dictionary<string, string> ColumnTypes(StruoDbType db, string conn, string table) =>
        Columns(db, conn, table).ToDictionary(c => c.Key, c => c.Value.DataType.ToLowerInvariant());

    [SqliteOnlyFact("SQLite twin of Shapes_resolve_to_the_live_backend_literals")]
    public async Task Shapes_resolve_to_the_sqlite_literals()
    {
        using var file = TestBackend.CreateSqliteDatabase();
        await Host(file.DbType, file.ConnectionString, "struo_", "Types").ApplyAsync(default);

        var types = ColumnTypes(file.DbType, file.ConnectionString, "struo_typeprobe");
        types["at"].Should().Be("timestamptz");
        types["ts"].Should().Be("datetime");
        types["body"].Should().Be("text");
        types["data"].Should().Be("text");
    }

    // Timestamp is the type and the fractional-second digits SqlSugar reports (in Length on MySQL/MariaDB,
    // in DecimalDigits on SQL Server).
    private static readonly Dictionary<string, (string Tz, string LongText, (string Type, int Length, int Scale) Timestamp)> LiveLiterals = new()
    {
        ["PostgreSQL"] = ("timestamptz", "text", ("timestamp", 0, 0)),
        ["SqlServer"] = ("datetimeoffset", "nvarchar", ("datetime", 23, 3)),
        ["MySql"] = ("datetime", "longtext", ("datetime", 6, 0)),
        ["MariaDb"] = ("datetime", "longtext", ("datetime", 6, 0)),
    };

    [Theory, MemberData(nameof(LiveBackends))]
    public async Task Shapes_resolve_to_the_live_backend_literals(string backend)
    {
        var (_, db, connection) = LiveBackend.Get(backend);
        var (tz, longText, timestamp) = LiveLiterals[backend];
        if (connection is not { } conn) return;
        LiveDatabases.GuardDisposable(conn);
        var prefix = "t" + Guid.NewGuid().ToString("N")[..8] + "_";
        var client = new SqlSugarClient(new ConnectionConfig
            { DbType = DbTypeMapper.Map(db), ConnectionString = conn, IsAutoCloseConnection = true });
        try { client.DbMaintenance.CreateDatabase(); } catch { /* exists */ }
        try
        {
            await Host(db, conn, prefix, "Types").ApplyAsync(default);

            var columns = Columns(db, conn, prefix + "typeprobe");
            var types = columns.ToDictionary(c => c.Key, c => c.Value.DataType.ToLowerInvariant());
            types["at"].Should().Be(tz);
            (types["ts"], columns["ts"].Length, columns["ts"].DecimalDigits).Should().Be(timestamp);
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
        using var database = TestBackend.CreateUnitDatabase();
        await Host(database.DbType, database.ConnectionString, "struo_", "Sidecar").ApplyAsync(default);

        var indexes = IndexCatalog.Read(new SqlSugarClient(database.RawConfig()), database.DbType, "probe_translations");
        var index = indexes.Should().ContainSingle(i =>
            string.Equals(i.Name, "ux_probe_translations_fk_locale", StringComparison.OrdinalIgnoreCase)).Which;
        index.IsUnique.Should().BeTrue();
        index.Columns.Should().Equal("probeid", "locale");
    }
}
