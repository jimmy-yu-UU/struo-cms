// tests/Struo.Tests/Query/ItemServiceTests.cs
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

        var collections = MetadataScanner.Scan(typeof(Article).Assembly);
        var provider = new CachedMetadataProvider(collections);
        var registry = new EntityRegistry(MetadataScanner.ScanDescriptors(
            [typeof(Article), typeof(Tag), typeof(Author), typeof(Category), typeof(ArticleTag)]));
        var repo = new SqlSugarItemRepository(db, registry);
        var collectionTypes = new Dictionary<string, Type>(StringComparer.OrdinalIgnoreCase)
        {
            ["article"] = typeof(Article),
            ["tag"] = typeof(Tag),
            ["author"] = typeof(Author),
            ["category"] = typeof(Category),
        };
        var graph = new RelationshipGraph(collections, collectionTypes);
        var expander = new RelationExpander(repo, graph);
        _svc = new ItemService(repo, provider, registry, new AllowAllPermissionService(),
            graph, expander, new StruoQueryOptions());
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
