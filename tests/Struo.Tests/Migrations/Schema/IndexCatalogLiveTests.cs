using AwesomeAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using SqlSugar;
using Struo.Application.Configuration;
using Struo.Infrastructure.Migrations;
using Struo.Infrastructure.Migrations.Schema;
using Struo.Infrastructure.Persistence;
using Struo.Tests.Support;
using Xunit;

namespace Struo.Tests.Migrations.Schema;

public sealed class IndexCatalogLiveTests
{
    public static TheoryData<string> Backends => LiveBackend.Names();

    internal static MigrationHost Host(StruoDbType db, string conn, string prefix) => new(
        new MigrationHostOptions(db, conn, prefix, [typeof(IndexCatalogLiveTests).Assembly], 30)
        { NamespaceFilter = "Struo.Tests.Migrations.Probes.IndexCatalog" },
        NullLoggerFactory.Instance);

    internal static void AssertProbeIndexes(IReadOnlyList<DbIndex> indexes, string table)
    {
        indexes.Should().Contain(i => i.Name == table + "_ux_ba" && i.IsUnique && i.Columns.SequenceEqual(new[] { "b", "a" }));
        indexes.Should().Contain(i => i.Name == table + "_ix_a" && !i.IsUnique && i.Columns.SequenceEqual(new[] { "a" }));
        indexes.Should().Contain(i => i.IsUnique && i.Columns.SequenceEqual(new[] { "id" }));
        indexes.Should().OnlyContain(i => i.Columns.All(c => c == c.ToLowerInvariant()));
    }

    [Theory, MemberData(nameof(Backends))]
    public async Task Catalog_reports_unique_key_order_non_unique_and_primary_key_indexes(string backend)
    {
        var (_, db, connection) = LiveBackend.Get(backend);
        if (connection is not { } conn) return;
        LiveDatabases.GuardDisposable(conn);
        var client = new SqlSugarClient(new ConnectionConfig
            { DbType = DbTypeMapper.Map(db), ConnectionString = conn, IsAutoCloseConnection = true });
        try { client.DbMaintenance.CreateDatabase(); } catch { /* exists or not permitted */ }
        var prefix = "t" + Guid.NewGuid().ToString("N")[..8] + "_";
        var table = prefix + "idxprobe";
        try
        {
            await Host(db, conn, prefix).ApplyAsync(default);

            var indexes = IndexCatalog.Read(client, db, table);

            AssertProbeIndexes(indexes, table);
        }
        finally
        {
            foreach (var t in new[] { table, prefix + "schema_versions" })
                if (client.DbMaintenance.IsAnyTable(t, false)) client.DbMaintenance.DropTable(t);
        }
    }
}
