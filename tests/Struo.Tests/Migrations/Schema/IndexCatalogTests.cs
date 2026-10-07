using AwesomeAssertions;
using SqlSugar;
using Struo.Application.Configuration;
using Struo.Infrastructure.Migrations.Schema;
using Struo.Tests.Support;
using Xunit;

namespace Struo.Tests.Migrations.Schema;

public sealed class IndexCatalogTests : IDisposable
{
    private readonly SqliteTestDatabase _file = new();

    public void Dispose() => _file.Dispose();

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

    [Fact]
    public async Task Sqlite_reports_unique_key_order_non_unique_and_primary_key_indexes()
    {
        var table = "t_idxprobe";
        await IndexCatalogLiveTests.Host(StruoDbType.Sqlite, _file.ConnectionString, "t_").ApplyAsync(default);
        var client = new SqlSugarClient(new ConnectionConfig
            { DbType = SqlSugar.DbType.Sqlite, ConnectionString = _file.ConnectionString, IsAutoCloseConnection = true });

        var indexes = IndexCatalog.Read(client, StruoDbType.Sqlite, table);

        IndexCatalogLiveTests.AssertProbeIndexes(indexes, table);
    }
}
