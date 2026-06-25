using FluentAssertions;
using Struo.Application.Configuration;
using Struo.Infrastructure.Persistence;
using Struo.Sample.Blog;
using Struo.Tests.Support;
using Xunit;

namespace Struo.Tests.Persistence;

public class AuditAopTests
{
    private static DatabaseOptions Options(SqliteTestDatabase db) => new()
    {
        DbType = StruoDbType.Sqlite,
        ConnectionString = db.ConnectionString
    };

    [Fact]
    public void Insert_stamps_created_and_updated_audit_fields()
    {
        using var db = new SqliteTestDatabase();
        var client = SqlSugarClientFactory.Create(Options(db), new TestCurrentUserAccessor("alice"));
        client.CodeFirst.InitTables<Article>();

        var id = client.Insertable(new Article { Title = "Hello" }).ExecuteReturnBigIdentity();

        var saved = client.Queryable<Article>().InSingle(id);
        saved.CreatedBy.Should().Be("alice");
        saved.UpdatedBy.Should().Be("alice");
        saved.CreatedAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromMinutes(1));
        saved.UpdatedAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromMinutes(1));
    }

    [Fact]
    public void Update_restamps_only_updated_audit_fields()
    {
        using var db = new SqliteTestDatabase();
        var client = SqlSugarClientFactory.Create(Options(db), new TestCurrentUserAccessor("alice"));
        client.CodeFirst.InitTables<Article>();
        var id = client.Insertable(new Article { Title = "Hello" }).ExecuteReturnBigIdentity();
        var original = client.Queryable<Article>().InSingle(id);

        var updateClient = SqlSugarClientFactory.Create(Options(db), new TestCurrentUserAccessor("bob"));
        var toUpdate = updateClient.Queryable<Article>().InSingle(id);
        toUpdate.Title = "Changed";
        updateClient.Updateable(toUpdate).ExecuteCommand();

        var after = updateClient.Queryable<Article>().InSingle(id);
        after.CreatedBy.Should().Be("alice");           // unchanged
        after.UpdatedBy.Should().Be("bob");             // restamped
        after.CreatedAt.Should().BeCloseTo(original.CreatedAt, TimeSpan.FromSeconds(1));
        after.UpdatedAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromMinutes(1)); // restamped
        after.UpdatedAt.Should().BeOnOrAfter(original.UpdatedAt);
    }
}
