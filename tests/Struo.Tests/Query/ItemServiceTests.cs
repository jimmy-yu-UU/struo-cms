// tests/Struo.Tests/Query/ItemServiceTests.cs
using AwesomeAssertions;
using Struo.Application.Configuration;
using Struo.Application.Query;
using Struo.Infrastructure.Metadata;
using Struo.Infrastructure.Persistence;
using Struo.Infrastructure.Query;
using Struo.Infrastructure.Localization;
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
        db.CodeFirst.InitTables<ArticleTranslation>();
        db.CodeFirst.InitTables<Language>();
        LanguageSeeder.SeedAsync(db).GetAwaiter().GetResult();

        var collections = MetadataScanner.ScanTypes(
            [typeof(Article), typeof(Tag), typeof(Author), typeof(Category), typeof(Struo.Infrastructure.Files.File)]);
        var provider = new CachedMetadataProvider(collections);
        var registry = new EntityRegistry(MetadataScanner.ScanDescriptors(
            [typeof(Article), typeof(Tag), typeof(Author), typeof(Category), typeof(ArticleTag), typeof(Struo.Infrastructure.Files.File)]));
        var collectionTypes = new Dictionary<string, Type>(StringComparer.OrdinalIgnoreCase)
        {
            ["article"] = typeof(Article),
            ["tag"] = typeof(Tag),
            ["author"] = typeof(Author),
            ["category"] = typeof(Category),
            ["file"] = typeof(Struo.Infrastructure.Files.File),
        };
        var graph = new RelationshipGraph(collections, collectionTypes);
        var repo = new SqlSugarItemRepository(db, registry, graph, provider, new StruoQueryOptions());
        var expander = new RelationExpander(repo, graph);
        var resolver = new RelationFilterResolver(repo, graph, provider, registry, new StruoQueryOptions());
        var languages = new LanguageProvider(db);
        _svc = new ItemService(repo, provider, registry, new AllowAllPermissionService(),
            graph, expander, graph, resolver, languages, new StruoQueryOptions());
    }

    public void Dispose() => _file.Dispose();

    [Fact]
    public async Task Create_projects_id_audit_and_camelCase_fields()
    {
        using var body = System.Text.Json.JsonDocument.Parse(
            """{"status":"draft","translations":{"en":{"title":"Hello"}}}""");
        var dict = await _svc.CreateAsync("article", body.RootElement);

        dict.Should().ContainKey("id");
        dict["status"].Should().Be("draft");
        dict.Should().ContainKey("createdAt");
        dict.Should().ContainKey("seoTitle");
    }
}
