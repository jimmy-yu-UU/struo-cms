using System.Text.RegularExpressions;
using AwesomeAssertions;
using SqlSugar;
using Struo.Application.Configuration;
using Struo.Infrastructure.Persistence;
using Struo.Tests.Support;
using Xunit;

namespace Struo.Tests.Query;

/// <summary>
/// The PostgreSQL-only part of the live suite: behaviour that exists only on Npgsql / PostgreSQL DDL.
/// A no-op pass when no PostgreSQL connection is configured. Run:
/// dotnet test --filter FullyQualifiedName~PostgresOnlyTests
/// </summary>
[Collection("LiveRepository")]
public sealed partial class PostgresOnlyTests : IClassFixture<PostgresOnlyDatabases>
{
    private readonly PostgresOnlyDatabases _databases;

    public PostgresOnlyTests(PostgresOnlyDatabases databases) => _databases = databases;

    private static string? Conn => LiveBackend.Get("PostgreSQL").Connection;

    // Guards the pooling fix against silent removal: PgTestConnectionString's own unit tests only
    // exercise DisablePooling in isolation, so this asserts the wiring in LiveDatabases. Matched case-
    // and whitespace-insensitively like DisablePooling's own detection. Npgsql-only: the pool is
    // process-wide and outlives each test's client (see docs/ai/decisions/pg-test-connection-pooling.md).
    [Fact]
    public void Resolved_connection_disables_pooling()
    {
        if (string.IsNullOrWhiteSpace(Conn)) return;
        Conn.Should().MatchRegex(
            PoolingDisabled(),
            "LiveDatabases must route the PostgreSQL connection through " +
            "PgTestConnectionString.DisablePooling. If you set Pooling yourself to re-investigate the " +
            "abort recorded in AGENTS.md, this test is the expected casualty of that choice; " +
            "otherwise the pooling fix has been dropped and the flake is back.");
    }

    // 這條測試證明：在真 PostgreSQL 上，未過濾的 InitTables 會 DROP 被移除的欄位；SQLite 不驗證宣告
    // 型別、其 dialect 的結構同步能力也與 PG 不同，所以這個主張在這個儲存庫裡只有在這裡才證得出來。
    // PostgreSQL DDL 行為，其他後端的 DROP COLUMN 語意不同，故不泛化。
    // 完整說明：docs/ai/decisions/migrations-are-the-only-schema-source.md。
    [Fact]
    public void Unfiltered_InitTables_drops_a_removed_column_on_postgres()
    {
        if (string.IsNullOrWhiteSpace(Conn)) return;
        var postgres = LiveBackend.Get("PostgreSQL");
        using var db = SqlSugarClientFactory.Create(
            new DatabaseOptions { DbType = postgres.Db, ConnectionString = _databases.ConnectionFor(postgres) },
            new TestCurrentUserAccessor(Guid.Empty));
        try
        {
            if (db.DbMaintenance.IsAnyTable("destructive_init_probe", false))
                db.DbMaintenance.DropTable("destructive_init_probe");

            db.CodeFirst.InitTables<DestructiveInitProbeWide>();
            db.DbMaintenance.GetColumnInfosByTableName("destructive_init_probe", false)
              .Select(c => c.DbColumnName.ToLowerInvariant())
              .Should().Contain("doomed", "前置條件：探針表必須先帶有這一欄");

            db.CodeFirst.InitTables<DestructiveInitProbeNarrow>();

            var columnsAfter = db.DbMaintenance.GetColumnInfosByTableName("destructive_init_probe", false)
              .Select(c => c.DbColumnName.ToLowerInvariant())
              .ToList();
            columnsAfter.Should().Contain("id", "同一次 rebuild 不應連帶丟失其他欄位");
            columnsAfter.Should().Contain("keep", "同一次 rebuild 不應連帶丟失其他欄位");
            columnsAfter.Should().NotContain("doomed",
                "entity 移除屬性後，未過濾的 InitTables 在 PostgreSQL 上 DROP COLUMN");
        }
        finally
        {
            try
            {
                if (db.DbMaintenance.IsAnyTable("destructive_init_probe", false))
                    db.DbMaintenance.DropTable("destructive_init_probe");
            }
            catch { /* best-effort cleanup; don't mask the real failure */ }
        }
    }

    [GeneratedRegex(@"Pooling\s*=\s*false", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex PoolingDisabled();
}
