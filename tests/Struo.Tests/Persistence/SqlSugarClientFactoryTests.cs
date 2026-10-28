// tests/Struo.Tests/Persistence/SqlSugarClientFactoryTests.cs
using SqlSugar;
using Struo.Application.Configuration;
using Struo.Infrastructure.Persistence;
using Struo.Tests.Support;
using Xunit;

namespace Struo.Tests.Persistence;

public class SqlSugarClientFactoryTests
{
    // GraphQL runs sibling root-field resolvers in parallel on the one registered ISqlSugarClient.
    // A plain SqlSugarClient is not thread-safe for that (interleaved ADO calls on shared connection
    // state, intermittent "connection already open" / 500s). SqlSugarScope is SqlSugar's thread-safe
    // wrapper (one inner client per async flow) and still implements ISqlSugarClient.
    [Fact]
    public void Create_returns_a_thread_safe_SqlSugarScope_not_a_bare_SqlSugarClient()
    {
        using var db = TestBackend.CreateUnitDatabase();
        var options = db.Options();

        var client = SqlSugarClientFactory.Create(options, new TestCurrentUserAccessor(null));

        Assert.IsType<SqlSugarScope>(client);
    }

    private static ISqlSugarClient NewClient(ITestDatabase db, string prefix) =>
        SqlSugarClientFactory.Create(
            db.Options(prefix),
            new TestCurrentUserAccessor(null));

    [Fact]
    public void Default_prefix_applies_to_framework_tables_only()
    {
        using var db = TestBackend.CreateUnitDatabase();
        var client = NewClient(db, "struo_");

        Assert.Equal("struo_users", client.EntityMaintenance.GetTableName<Struo.Infrastructure.Identity.User>());
        Assert.Equal("fx_articles", client.EntityMaintenance.GetTableName<Struo.Tests.Fixtures.FxArticle>());
    }

    [Fact]
    public void Empty_prefix_yields_the_attribute_names()
    {
        using var db = TestBackend.CreateUnitDatabase();
        var client = NewClient(db, "");

        Assert.Equal("users", client.EntityMaintenance.GetTableName<Struo.Infrastructure.Identity.User>());
    }

    // SqlSugar caches EntityInfo per process, keyed by ConnectionConfig.ConfigId. Two clients with
    // different prefixes in one test process must therefore not share a cache entry.
    [Fact]
    public void Two_clients_with_different_prefixes_resolve_independently_in_one_process()
    {
        using var dbA = TestBackend.CreateUnitDatabase();
        using var dbB = TestBackend.CreateUnitDatabase();
        var a = NewClient(dbA, "aa_");
        var b = NewClient(dbB, "bb_");

        Assert.Equal("aa_users", a.EntityMaintenance.GetTableName<Struo.Infrastructure.Identity.User>());
        Assert.Equal("bb_users", b.EntityMaintenance.GetTableName<Struo.Infrastructure.Identity.User>());
        Assert.Equal("aa_users", a.EntityMaintenance.GetTableName<Struo.Infrastructure.Identity.User>());
    }

    // One process can hold clients for several providers with the same prefix: each resolves the
    // [ColumnShape] column types of its own provider, whichever client reads the entity first.
    [Theory]
    [InlineData(StruoDbType.Sqlite, StruoDbType.SqlServer)]
    [InlineData(StruoDbType.SqlServer, StruoDbType.Sqlite)]
    [InlineData(StruoDbType.PostgreSQL, StruoDbType.MySql)]
    [InlineData(StruoDbType.MySql, StruoDbType.PostgreSQL)]
    public void Clients_for_two_providers_with_one_prefix_resolve_their_own_column_types(StruoDbType first, StruoDbType second)
    {
        var prefix = "x" + Guid.NewGuid().ToString("N")[..8] + "_";

        var firstType = ShapedColumnType(first, prefix);
        var secondType = ShapedColumnType(second, prefix);

        Assert.Equal(ColumnTypeMap.For(ColumnShape.TimestampWithTimeZone, DbTypeMapper.Map(first)), firstType);
        Assert.Equal(ColumnTypeMap.For(ColumnShape.TimestampWithTimeZone, DbTypeMapper.Map(second)), secondType);
    }

    // Entity info is resolved without opening a connection, so no server is needed.
    private static string ShapedColumnType(StruoDbType dbType, string prefix) =>
        SqlSugarClientFactory.Create(
                new DatabaseOptions { DbType = dbType, ConnectionString = "Data Source=unused", TablePrefix = prefix },
                new TestCurrentUserAccessor(null))
            .EntityMaintenance.GetEntityInfo<Struo.Infrastructure.Files.MediaFolder>()
            .Columns.Single(c => c.PropertyName == nameof(Struo.Infrastructure.Files.MediaFolder.CreatedAt))
            .DataType;

    [Fact]
    public void InitTables_creates_the_prefixed_table_and_queries_hit_it()
    {
        using var db = TestBackend.CreateUnitDatabase();
        var client = NewClient(db, "struo_");

        client.CodeFirst.InitTables<Struo.Infrastructure.Identity.Role>();
        var tables = client.DbMaintenance.GetTableInfoList(false).Select(t => t.Name.ToLowerInvariant()).ToList();
        Assert.Contains("struo_roles", tables);
        Assert.DoesNotContain("roles", tables);

        client.Insertable(new Struo.Infrastructure.Identity.Role { Id = Guid.NewGuid(), Name = "editor" }).ExecuteCommand();
        Assert.Equal(1, client.Queryable<Struo.Infrastructure.Identity.Role>().Count());
    }

    [Fact]
    public void SqlServer_clients_create_nvarchar_string_columns_with_CodeFirst()
    {
        var client = SqlSugarClientFactory.Create(
            new DatabaseOptions
            {
                DbType = StruoDbType.SqlServer,
                ConnectionString = "Server=unused;Database=unused-test;",
                TablePrefix = "nv_"
            },
            new TestCurrentUserAccessor(null));

        Assert.True(client.CurrentConnectionConfig.MoreSettings?.SqlServerCodeFirstNvarchar);
    }
}
