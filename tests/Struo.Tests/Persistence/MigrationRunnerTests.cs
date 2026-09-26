using AwesomeAssertions;
using SqlSugar;
using Struo.Application.Configuration;
using Struo.Infrastructure.Persistence;
using Struo.Tests.Support;
using Xunit;

namespace Struo.Tests.Persistence;

public sealed class MigrationRunnerTests
{
    // ---- Pure ordering/filtering logic (unit-testable without any database) ----

    [Fact]
    public void OrderSqlFiles_sorts_by_ordinal_filename_and_ignores_non_sql()
    {
        var input = new[]
        {
            "009-hot-path-indexes.sql",
            "002-retroactive-add-version-columns.sql",
            "README.md",
            "001-widen-content-bearing-text-columns.sql",
            "notes.txt",
            "010-later.sql",
        };

        var ordered = MigrationRunner.OrderSqlFiles(input);

        ordered.Should().ContainInOrder(
            "001-widen-content-bearing-text-columns.sql",
            "002-retroactive-add-version-columns.sql",
            "009-hot-path-indexes.sql",
            "010-later.sql");
        ordered.Should().NotContain("README.md");
        ordered.Should().NotContain("notes.txt");
    }

    [Fact]
    public void SelectPending_skips_already_applied_and_preserves_order()
    {
        var ordered = new[] { "001-a.sql", "002-b.sql", "003-c.sql", "004-d.sql" };
        var applied = new HashSet<string>(StringComparer.Ordinal) { "001-a.sql", "003-c.sql" };

        var pending = MigrationRunner.SelectPending(ordered, applied);

        pending.Should().ContainInOrder("002-b.sql", "004-d.sql");
        pending.Should().HaveCount(2);
    }

    [Fact]
    public void SelectPending_returns_empty_when_all_applied()
    {
        var ordered = new[] { "001-a.sql", "002-b.sql" };
        var applied = new HashSet<string>(StringComparer.Ordinal) { "001-a.sql", "002-b.sql" };

        MigrationRunner.SelectPending(ordered, applied).Should().BeEmpty();
    }

    // ---- Runner behaviour on a non-PostgreSQL backend: applies normally ----

    private static (SqliteTestDatabase, ISqlSugarClient) NewClient()
    {
        var file = new SqliteTestDatabase();
        var client = SqlSugarClientFactory.Create(
            new DatabaseOptions { DbType = StruoDbType.Sqlite, ConnectionString = file.ConnectionString },
            new TestCurrentUserAccessor(Guid.Empty));
        return (file, client);
    }

    [Fact]
    public async Task ApplyAsync_applies_scripts_on_a_non_PostgreSQL_backend()
    {
        var (file, client) = NewClient();
        var dir = Directory.CreateTempSubdirectory("struo_mig_");
        using (file)
        {
            try
            {
                await File.WriteAllTextAsync(
                    Path.Combine(dir.FullName, "001-create-widget.sql"),
                    "CREATE TABLE widget (id integer);");

                var applied = await MigrationRunner.ApplyAsync(client, dir.FullName, logger: null);

                applied.Should().ContainSingle().Which.Should().Be("001-create-widget.sql");

                var tables = client.DbMaintenance.GetTableInfoList(false);
                tables.Any(t => t.Name.Equals("widget", StringComparison.OrdinalIgnoreCase))
                      .Should().BeTrue("the runner must no longer be a PostgreSQL-only no-op");
                var tracking = client.EntityMaintenance.GetTableName<SchemaMigration>();
                tables.Any(t => t.Name.Equals(tracking, StringComparison.OrdinalIgnoreCase))
                      .Should().BeTrue("the tracking table is created via CodeFirst on any backend");
            }
            finally { dir.Delete(recursive: true); }
        }
    }

    [Fact]
    public async Task ApplyAsync_is_idempotent_across_runs()
    {
        var (file, client) = NewClient();
        var dir = Directory.CreateTempSubdirectory("struo_mig_");
        using (file)
        {
            try
            {
                await File.WriteAllTextAsync(
                    Path.Combine(dir.FullName, "001-create-widget.sql"),
                    "CREATE TABLE widget (id integer);");

                (await MigrationRunner.ApplyAsync(client, dir.FullName, logger: null))
                    .Should().ContainSingle();

                // 第二次執行不得重跑（重跑會因 widget 已存在而丟例外）。
                (await MigrationRunner.ApplyAsync(client, dir.FullName, logger: null))
                    .Should().BeEmpty("already-recorded filenames are skipped");
            }
            finally { dir.Delete(recursive: true); }
        }
    }

    [Fact]
    public async Task ApplyAsync_records_the_applied_filename_in_the_tracking_entity()
    {
        var (file, client) = NewClient();
        var dir = Directory.CreateTempSubdirectory("struo_mig_");
        using (file)
        {
            try
            {
                await File.WriteAllTextAsync(
                    Path.Combine(dir.FullName, "001-create-widget.sql"),
                    "CREATE TABLE widget (id integer);");

                await MigrationRunner.ApplyAsync(client, dir.FullName, logger: null);

                var rows = await client.Queryable<SchemaMigration>().ToListAsync();
                rows.Should().ContainSingle();
                rows[0].Filename.Should().Be("001-create-widget.sql");
                rows[0].AppliedAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromMinutes(1));
            }
            finally { dir.Delete(recursive: true); }
        }
    }

    [Fact]
    public async Task Tracking_table_carries_the_configured_prefix()
    {
        var (file, client) = NewClient(); // NewClient uses the factory with default options
        var dir = Directory.CreateTempSubdirectory("struo_mig_");
        using (file)
        {
            try
            {
                await MigrationRunner.ApplyAsync(client, dir.FullName, logger: null);
                client.DbMaintenance.GetTableInfoList(false)
                    .Select(t => t.Name.ToLowerInvariant())
                    .Should().Contain("struo_schema_migrations");
            }
            finally { dir.Delete(recursive: true); }
        }
    }
}
