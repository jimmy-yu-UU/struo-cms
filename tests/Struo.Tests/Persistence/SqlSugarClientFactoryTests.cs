// tests/Struo.Tests/Persistence/SqlSugarClientFactoryTests.cs
using SqlSugar;
using Struo.Application.Configuration;
using Struo.Infrastructure.Persistence;
using Struo.Tests.Support;
using Xunit;

namespace Struo.Tests.Persistence;

public class SqlSugarClientFactoryTests
{
    // GraphQL pins query/mutation roots to DependencyInjectionScope.Request, so HotChocolate
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

    private static ISqlSugarClient NewClient(SqliteTestDatabase db, string prefix) =>
        SqlSugarClientFactory.Create(
            new DatabaseOptions { DbType = StruoDbType.Sqlite, ConnectionString = db.ConnectionString, TablePrefix = prefix },
            new TestCurrentUserAccessor(null));

    [Fact]
    public void Default_prefix_applies_to_framework_tables_only()
    {
        using var db = new SqliteTestDatabase();
        var client = NewClient(db, "struo_");

        Assert.Equal("struo_users", client.EntityMaintenance.GetTableName<Struo.Infrastructure.Identity.User>());
        Assert.Equal("struo_schema_migrations", client.EntityMaintenance.GetTableName<SchemaMigration>());
        Assert.Equal("articles", client.EntityMaintenance.GetTableName<Struo.Sample.Blog.Article>());
    }

    [Fact]
    public void Empty_prefix_yields_the_attribute_names()
    {
        using var db = new SqliteTestDatabase();
        var client = NewClient(db, "");

        Assert.Equal("users", client.EntityMaintenance.GetTableName<Struo.Infrastructure.Identity.User>());
        Assert.Equal("schema_migrations", client.EntityMaintenance.GetTableName<SchemaMigration>());
    }

    // SqlSugar caches EntityInfo per process, keyed by ConnectionConfig.ConfigId. Two clients with
    // different prefixes in one test process must therefore not share a cache entry.
    [Fact]
    public void Two_clients_with_different_prefixes_resolve_independently_in_one_process()
    {
        using var dbA = new SqliteTestDatabase();
        using var dbB = new SqliteTestDatabase();
        var a = NewClient(dbA, "aa_");
        var b = NewClient(dbB, "bb_");

        Assert.Equal("aa_users", a.EntityMaintenance.GetTableName<Struo.Infrastructure.Identity.User>());
        Assert.Equal("bb_users", b.EntityMaintenance.GetTableName<Struo.Infrastructure.Identity.User>());
        Assert.Equal("aa_users", a.EntityMaintenance.GetTableName<Struo.Infrastructure.Identity.User>());
    }

    [Fact]
    public void InitTables_creates_the_prefixed_table_and_queries_hit_it()
    {
        using var db = new SqliteTestDatabase();
        var client = NewClient(db, "struo_");

        client.CodeFirst.InitTables(typeof(Struo.Infrastructure.Identity.Role));
        var tables = client.DbMaintenance.GetTableInfoList(false).Select(t => t.Name.ToLowerInvariant()).ToList();
        Assert.Contains("struo_roles", tables);
        Assert.DoesNotContain("roles", tables);

        client.Insertable(new Struo.Infrastructure.Identity.Role { Id = Guid.NewGuid(), Name = "editor" }).ExecuteCommand();
        Assert.Equal(1, client.Queryable<Struo.Infrastructure.Identity.Role>().Count());
    }
}
