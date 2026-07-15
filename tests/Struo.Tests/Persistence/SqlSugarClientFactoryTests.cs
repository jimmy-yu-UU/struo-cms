// tests/Struo.Tests/Persistence/SqlSugarClientFactoryTests.cs
using SqlSugar;
using Struo.Application.Configuration;
using Struo.Infrastructure.Persistence;
using Struo.Tests.Support;
using Xunit;

namespace Struo.Tests.Persistence;

public class SqlSugarClientFactoryTests
{
    // CS-1: GraphQL pins query/mutation roots to DependencyInjectionScope.Request, so HotChocolate
    // can run sibling root-field resolvers on separate threads that all share one scoped
    // ISqlSugarClient. Plain SqlSugarClient is not thread-safe (interleaved ADO calls on shared
    // connection state -> intermittent "connection already open" / 500s under concurrency).
    // SqlSugarScope is SqlSugar's official thread-safe wrapper (AsyncLocal-managed inner contexts)
    // and still implements ISqlSugarClient, so this is a drop-in fix at the factory boundary.
    [Fact]
    public void Create_returns_a_thread_safe_SqlSugarScope_not_a_bare_SqlSugarClient()
    {
        using var db = new SqliteTestDatabase();
        var options = new DatabaseOptions
        {
            DbType = StruoDbType.Sqlite,
            ConnectionString = db.ConnectionString
        };

        var client = SqlSugarClientFactory.Create(options, new TestCurrentUserAccessor(null));

        Assert.IsType<SqlSugarScope>(client);
    }
}
