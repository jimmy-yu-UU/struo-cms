using AwesomeAssertions;
using SqlSugar;
using Struo.Application.Configuration;
using Struo.Infrastructure.Metadata;
using Struo.Infrastructure.Persistence;
using Struo.Sample.Blog;
using Struo.Tests.Support;
using Xunit;

namespace Struo.Tests.Persistence;

public class DatabaseInitializerTests
{
    private static (SqliteTestDatabase, ISqlSugarClient) NewClient()
    {
        var db = new SqliteTestDatabase();
        var client = SqlSugarClientFactory.Create(
            new DatabaseOptions { DbType = StruoDbType.Sqlite, ConnectionString = db.ConnectionString },
            new TestCurrentUserAccessor(Guid.Empty));
        return (db, client);
    }

    private static ISet<string> Snapshot(ISqlSugarClient client) =>
        client.DbMaintenance.GetTableInfoList(false)
              .Select(t => t.Name.ToLowerInvariant())
              .ToHashSet(StringComparer.Ordinal);

    private static bool TableExists(ISqlSugarClient client, string table) =>
        client.DbMaintenance.GetTableInfoList(false)
              .Any(t => t.Name.Equals(table, StringComparison.OrdinalIgnoreCase));

    private static int ColumnCount(ISqlSugarClient client, string table) =>
        client.DbMaintenance.GetColumnInfosByTableName(table, false).Count;

    // ---- CreateMissingTables ----

    [Fact]
    public void CreateMissingTables_creates_a_table_that_does_not_exist()
    {
        var (db, client) = NewClient();
        using (db)
        {
            var created = DatabaseInitializer.CreateMissingTables(
                client, Snapshot(client), logger: null, typeof(Article));

            created.Should().ContainSingle().Which.Should().Be(typeof(Article));
            TableExists(client, "articles").Should().BeTrue();
        }
    }

    [Fact]
    public void CreateMissingTables_runs_outside_Development()
    {
        // 沒有環境參數即是設計意圖：建立職責不受環境限制。此測試釘住該行為。
        var (db, client) = NewClient();
        using (db)
        {
            var act = () => DatabaseInitializer.CreateMissingTables(
                client, Snapshot(client), logger: null, typeof(Article));

            act.Should().NotThrow();
            TableExists(client, "articles").Should().BeTrue();
        }
    }

    [Fact]
    public void CreateMissingTables_never_touches_an_existing_table()
    {
        var (db, client) = NewClient();
        using (db)
        {
            // 刻意建一張「欄位比 entity 少」的表，若實作誤用完整 InitTables 就會被補欄位。
            client.Ado.ExecuteCommand("CREATE TABLE articles (id TEXT PRIMARY KEY)");
            var before = ColumnCount(client, "articles");
            before.Should().Be(1);

            var created = DatabaseInitializer.CreateMissingTables(
                client, Snapshot(client), logger: null, typeof(Article));

            created.Should().BeEmpty();
            ColumnCount(client, "articles").Should().Be(before,
                "既有表永遠不得被 CreateMissingTables 改動");
        }
    }

    [Fact]
    public void CreateMissingTables_creates_only_the_missing_subset()
    {
        var (db, client) = NewClient();
        using (db)
        {
            client.Ado.ExecuteCommand("CREATE TABLE articles (id TEXT PRIMARY KEY)");

            var created = DatabaseInitializer.CreateMissingTables(
                client, Snapshot(client), logger: null, typeof(Article), typeof(Tag));

            created.Should().ContainSingle().Which.Should().Be(typeof(Tag));
            ColumnCount(client, "articles").Should().Be(1);
            TableExists(client, "tags").Should().BeTrue();
        }
    }

    [Fact]
    public void CreateMissingTables_creates_a_table_for_every_core_framework_entity()
    {
        // The central claim of the CodeFirst/Migration split: table creation for the full core set is
        // uniform, automatic, and not dependent on any hand-maintained SQL file. Pinned positively —
        // every FrameworkEntityTypes.All type must resolve to an actual table afterward.
        var (db, client) = NewClient();
        using (db)
        {
            DatabaseInitializer.CreateMissingTables(
                client, Snapshot(client), logger: null, FrameworkEntityTypes.All.ToArray());

            foreach (var entityType in FrameworkEntityTypes.All)
            {
                var tableName = client.EntityMaintenance.GetTableName(entityType);
                TableExists(client, tableName).Should().BeTrue(
                    $"CreateMissingTables must create a table for core entity '{entityType.Name}' " +
                    $"(expected table '{tableName}')");
            }
        }
    }

    // ---- SyncSchema ----

    [Fact]
    public void SyncSchema_runs_in_Development_and_alters_an_existing_table()
    {
        var (db, client) = NewClient();
        using (db)
        {
            client.Ado.ExecuteCommand("CREATE TABLE articles (id TEXT PRIMARY KEY)");

            var ran = DatabaseInitializer.SyncSchema(
                client, new FakeHostEnvironment("Development"), logger: null, typeof(Article));

            ran.Should().BeTrue();
            ColumnCount(client, "articles").Should().BeGreaterThan(1,
                "Development 的完整同步應補齊 entity 上的欄位");
        }
    }

    [Fact]
    public void SyncSchema_is_ignored_outside_Development()
    {
        var (db, client) = NewClient();
        using (db)
        {
            client.Ado.ExecuteCommand("CREATE TABLE articles (id TEXT PRIMARY KEY)");

            var ran = DatabaseInitializer.SyncSchema(
                client, new FakeHostEnvironment("Production"), logger: null, typeof(Article));

            ran.Should().BeFalse();
            ColumnCount(client, "articles").Should().Be(1,
                "非 Development 必須完全不動 schema");
        }
    }

    // ---- 未過濾的 InitTables：SQLite 上的實測行為 ----

    [Fact]
    public void Unfiltered_InitTables_does_not_drop_columns_on_Sqlite()
    {
        // 實測（2026-08-03，SqlSugarCore 5.1.4.215）：在 SQLite 上，欄位沒有被刪掉。 narrative-guard:allow: evidence date; decision file deferred
        // 因此「InitTables 會 DROP COLUMN」這個架構前提無法用 SQLite 測套件證明；PostgreSQL 端的量測
        // 已經完成：同一組探針型別在真 PostgreSQL 上跑出相反的結果——Doomed 欄位
        // 被 DROP 掉了，見 Struo.Tests.Query.PostgresIntegrationTests
        // .Unfiltered_InitTables_drops_a_removed_column_on_postgres（opt-in，僅在設定 PG 連線時執行）。
        // 已知成因，不是「SQLite 做不到」：SqlSugar 的 SqliteCodeFirst.ExistLogic（上游 tag 5.1.4.197，
        // Src/Asp.NetCore2/SqlSugar/Realization/Sqlite/CodeFirst/SqliteCodeFirst.cs:10,50-58）確實實作了
        // DROP COLUMN，但把關在 ConnectionConfig.MoreSettings.SqliteCodeFirstEnableDropColumn 之後；
        // SqlSugarClientFactory 從未設定過 MoreSettings（`grep -rn "MoreSettings" src/ tests/` 除了說明
        // 文件裡的散文引用（本行也是其中之一）外沒有命中），所以這個判斷永遠是 false。這是設定造成的，
        // 不是 SQLite 引擎或 SqlSugar 的
        // SQLite dialect 本身的極限——這條測試的作用是把「在這個儲存庫目前的設定下，SQLite 上到底會不會」
        // 從未知釘成事實：若哪天有人打開那個旗標，這裡會紅，我們就知道 CI 套件的證明力改變了。
        var (db, client) = NewClient();
        using (db)
        {
            client.CodeFirst.InitTables(typeof(DestructiveInitProbeWide));
            client.DbMaintenance.GetColumnInfosByTableName("destructive_init_probe", false)
                  .Select(c => c.DbColumnName)
                  .Should().Contain("Doomed", "前置條件：探針表必須先帶有這一欄");

            client.CodeFirst.InitTables(typeof(DestructiveInitProbeNarrow));

            var columns = client.DbMaintenance.GetColumnInfosByTableName("destructive_init_probe", false)
                  .Select(c => c.DbColumnName).ToList();
            columns.Should().Contain("Doomed");
            columns.Should().Contain("Id", "其他欄位不該在同一次 rebuild 中被意外弄丟");
            columns.Should().Contain("Keep", "其他欄位不該在同一次 rebuild 中被意外弄丟");
        }
    }

    [Fact]
    public void SyncSchema_in_Development_adds_a_column_to_an_existing_table_through_the_public_seam()
    {
        // 與 :128 的 SyncSchema_runs_in_Development_and_alters_an_existing_table 不同之處：那條測試
        // 只斷言欄位數變多，證明力較弱（欄位數字對得上也可能是巧合）。這條測試改用共用探針對出明確
        // 欄位名，並帶精確的前置條件（表必須先「不含」Doomed），因此能證明 DDL 確實透過
        // DatabaseInitializer.SyncSchema 這個公開入口流到既有表——反向探針方向，
        // 與 Unfiltered_InitTables_does_not_drop_columns_on_Sqlite（刪除面）互補。
        var (db, client) = NewClient();
        using (db)
        {
            client.CodeFirst.InitTables(typeof(DestructiveInitProbeNarrow));
            client.DbMaintenance.GetColumnInfosByTableName("destructive_init_probe", false)
                  .Select(c => c.DbColumnName)
                  .Should().NotContain("Doomed", "前置條件：探針表一開始不能有這一欄");

            var ran = DatabaseInitializer.SyncSchema(
                client, new FakeHostEnvironment("Development"), logger: null,
                typeof(DestructiveInitProbeWide));

            ran.Should().BeTrue();
            client.DbMaintenance.GetColumnInfosByTableName("destructive_init_probe", false)
                  .Select(c => c.DbColumnName)
                  .Should().Contain("Doomed");
        }
    }
}
