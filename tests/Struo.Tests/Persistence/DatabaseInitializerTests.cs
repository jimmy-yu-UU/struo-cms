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
}
