using AwesomeAssertions;
using Struo.Application.Configuration;
using Struo.Infrastructure.Persistence;
using Struo.Sample.Blog;
using Struo.Tests.Support;
using Xunit;

namespace Struo.Tests.Persistence;

public class AuditAopTests
{
    private static readonly Guid Alice = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid Bob   = Guid.Parse("22222222-2222-2222-2222-222222222222");

    private static DatabaseOptions Options(SqliteTestDatabase db) => new()
    {
        DbType = StruoDbType.Sqlite,
        ConnectionString = db.ConnectionString
    };

    [Fact]
    public void Insert_stamps_created_and_updated_audit_fields()
    {
        using var db = new SqliteTestDatabase();
        var client = SqlSugarClientFactory.Create(Options(db), new TestCurrentUserAccessor(Alice));
        client.CodeFirst.InitTables<Article>();

        var article = new Article { Id = Guid.CreateVersion7(), Status = "draft" };
        client.Insertable(article).ExecuteCommand();

        var saved = client.Queryable<Article>().InSingle(article.Id);
        saved.CreatedBy.Should().Be(Alice);
        saved.UpdatedBy.Should().Be(Alice);
        saved.CreatedAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromMinutes(1));
        saved.UpdatedAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromMinutes(1));
    }

    [Fact]
    public void Update_restamps_only_updated_audit_fields()
    {
        using var db = new SqliteTestDatabase();
        var client = SqlSugarClientFactory.Create(Options(db), new TestCurrentUserAccessor(Alice));
        client.CodeFirst.InitTables<Article>();
        var article = new Article { Id = Guid.CreateVersion7(), Status = "draft" };
        client.Insertable(article).ExecuteCommand();
        var original = client.Queryable<Article>().InSingle(article.Id);

        var updateClient = SqlSugarClientFactory.Create(Options(db), new TestCurrentUserAccessor(Bob));
        var toUpdate = updateClient.Queryable<Article>().InSingle(article.Id);
        toUpdate.Status = "published";
        updateClient.Updateable(toUpdate).ExecuteCommand();

        var after = updateClient.Queryable<Article>().InSingle(article.Id);
        after.CreatedBy.Should().Be(Alice);   // unchanged
        after.UpdatedBy.Should().Be(Bob);     // restamped
        after.CreatedAt.Should().BeCloseTo(original.CreatedAt, TimeSpan.FromSeconds(1));
        after.UpdatedAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromMinutes(1));
        after.UpdatedAt.Should().BeOnOrAfter(original.UpdatedAt);
    }
}
