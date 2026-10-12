using AwesomeAssertions;
using SqlSugar;
using Struo.Application.Configuration;
using Struo.Infrastructure.Migrations.Schema;
using Struo.Infrastructure.Localization;
using Struo.Infrastructure.Persistence;
using Struo.Tests.Support;
using Xunit;

namespace Struo.Tests.Migrations.Schema;

public sealed class IndexCatalogTests
{
    [Fact]
    public void Oracle_is_unsupported()
    {
        IndexCatalog.IsSupported(StruoDbType.Oracle).Should().BeFalse();
        IndexCatalog.Sql(StruoDbType.Oracle).Should().BeNull();
    }

    [Theory]
    [InlineData(StruoDbType.Sqlite)]
    [InlineData(StruoDbType.PostgreSQL)]
    [InlineData(StruoDbType.SqlServer)]
    [InlineData(StruoDbType.MySql)]
    public void Supported_backends_have_a_parameterised_query(StruoDbType db)
    {
        IndexCatalog.IsSupported(db).Should().BeTrue();
        IndexCatalog.Sql(db).Should().Contain("@table").And.Contain("column_name");
    }

    [SqliteOnlyFact("SQLite twin of IndexCatalogLiveTests.Catalog_reports_unique_key_order_non_unique_and_primary_key_indexes")]
    public async Task Sqlite_reports_unique_key_order_non_unique_and_primary_key_indexes()
    {
        using var file = TestBackend.CreateSqliteDatabase();
        var table = "t_idxprobe";
        await IndexCatalogLiveTests.Host(StruoDbType.Sqlite, file.ConnectionString, "t_").ApplyAsync(default);
        var client = new SqlSugarClient(file.RawConfig());

        var indexes = IndexCatalog.Read(client, StruoDbType.Sqlite, table);

        IndexCatalogLiveTests.AssertProbeIndexes(indexes, table);
    }

    [SqliteOnlyFact("SQLite rowid primary key")]
    public void Sqlite_rowid_primary_key_is_reported_as_a_unique_index()
    {
        using var file = TestBackend.CreateSqliteDatabase();
        var client = SqlSugarClientFactory.Create(file.Options("rw_"), new TestCurrentUserAccessor(Guid.NewGuid()));
        client.CodeFirst.InitTables<Language>();

        var indexes = IndexCatalog.Read(client, StruoDbType.Sqlite, "rw_languages");

        indexes.Should().Contain(i => i.IsUnique && i.Columns.SequenceEqual(new[] { "id" }));
    }

    [SqliteOnlyFact("SQLite expression and partial index DDL")]
    public void Sqlite_expression_and_partial_indexes_are_skipped()
    {
        using var file = TestBackend.CreateSqliteDatabase();
        var client = new SqlSugarClient(file.RawConfig());
        client.Ado.ExecuteCommand("CREATE TABLE ex (id TEXT PRIMARY KEY, a TEXT, b TEXT)");
        client.Ado.ExecuteCommand("CREATE UNIQUE INDEX ex_expr ON ex (lower(a))");
        client.Ado.ExecuteCommand("CREATE UNIQUE INDEX ex_part ON ex (b) WHERE a IS NOT NULL");
        client.Ado.ExecuteCommand("CREATE UNIQUE INDEX ex_plain ON ex (a, b)");

        var indexes = IndexCatalog.Read(client, StruoDbType.Sqlite, "ex");

        indexes.Select(i => i.Name).Should().NotContain(["ex_expr", "ex_part"]).And.Contain("ex_plain");
    }
}
