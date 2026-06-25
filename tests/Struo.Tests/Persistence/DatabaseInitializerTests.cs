using AwesomeAssertions;
using Struo.Application.Configuration;
using Struo.Infrastructure.Persistence;
using Struo.Sample.Blog;
using Struo.Tests.Support;
using Xunit;

namespace Struo.Tests.Persistence;

public class DatabaseInitializerTests
{
    private static (SqliteTestDatabase, SqlSugar.ISqlSugarClient) NewClient()
    {
        var db = new SqliteTestDatabase();
        var client = SqlSugarClientFactory.Create(
            new DatabaseOptions { DbType = StruoDbType.Sqlite, ConnectionString = db.ConnectionString },
            new TestCurrentUserAccessor("system"));
        return (db, client);
    }

    [Fact]
    public void Creates_table_when_environment_is_development()
    {
        var (db, client) = NewClient();
        using (db)
        {
            DatabaseInitializer.InitializeDevelopmentSchema(
                client, new FakeHostEnvironment("Development"), typeof(Article));

            var tables = client.DbMaintenance.GetTableInfoList(false);
            tables.Any(t => t.Name.Equals("articles", StringComparison.OrdinalIgnoreCase))
                  .Should().BeTrue();
        }
    }

    [Fact]
    public void Throws_when_environment_is_not_development()
    {
        var (db, client) = NewClient();
        using (db)
        {
            var act = () => DatabaseInitializer.InitializeDevelopmentSchema(
                client, new FakeHostEnvironment("Production"), typeof(Article));

            act.Should().Throw<InvalidOperationException>();
        }
    }
}
