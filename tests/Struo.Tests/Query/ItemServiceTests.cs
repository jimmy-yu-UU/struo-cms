// tests/Struo.Tests/Query/ItemServiceTests.cs
#pragma warning disable CS0618 // AllowAllPermissionService is intentionally used in tests
using AwesomeAssertions;
using Struo.Application.Configuration;
using Struo.Application.Query;
using Struo.Infrastructure.Metadata;
using Struo.Infrastructure.Persistence;
using Struo.Infrastructure.Query;
using Struo.Infrastructure.Security;
using Struo.Sample.Blog;
using Struo.Tests.Support;
using SqlSugar;
using Xunit;

namespace Struo.Tests.Query;

public class ItemServiceTests : IDisposable
{
    private readonly SqliteTestDatabase _file = new();
    private readonly ItemService _svc;

    public ItemServiceTests()
    {
        var db = SqlSugarClientFactory.Create(
            new DatabaseOptions { DbType = StruoDbType.Sqlite, ConnectionString = _file.ConnectionString },
            new TestCurrentUserAccessor("tester"));
        db.CodeFirst.InitTables<Article>();

        var provider = new CachedMetadataProvider(MetadataScanner.Scan(typeof(Article).Assembly));
        var registry = new EntityRegistry(MetadataScanner.ScanDescriptors([typeof(Article), typeof(Tag)]));
        var repo = new SqlSugarItemRepository(db, registry);
        _svc = new ItemService(repo, provider, registry, new AllowAllPermissionService(), new StruoQueryOptions());
    }

    public void Dispose() => _file.Dispose();

    [Fact]
    public async Task Create_projects_id_audit_and_camelCase_fields()
    {
        using var body = System.Text.Json.JsonDocument.Parse("""{"title":"Hello","status":"draft"}""");
        var dict = await _svc.CreateAsync("article", body.RootElement);

        dict.Should().ContainKey("id");
        dict["title"].Should().Be("Hello");
        dict.Should().ContainKey("createdAt");
        dict.Should().ContainKey("seoTitle");
    }
}
