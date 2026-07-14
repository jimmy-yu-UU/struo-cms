// tests/Struo.Tests/Query/ItemServiceRichTextSanitizationTests.cs
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
using Xunit;

namespace Struo.Tests.Query;

public class ItemServiceRichTextSanitizationTests : IDisposable
{
    private readonly SqliteTestDatabase _file = new();
    private readonly ItemService _svc;

    public ItemServiceRichTextSanitizationTests()
    {
        var db = SqlSugarClientFactory.Create(
            new DatabaseOptions { DbType = StruoDbType.Sqlite, ConnectionString = _file.ConnectionString },
            new TestCurrentUserAccessor(Guid.Empty));
        db.CodeFirst.InitTables<Article>();
        db.CodeFirst.InitTables<ArticleTranslation>();
        db.CodeFirst.InitTables<Language>();
        db.CodeFirst.InitTables<Tag>();
        db.CodeFirst.InitTables<ArticleTag>();
        db.CodeFirst.InitTables<Category>();
        LanguageSeeder.SeedAsync(db).GetAwaiter().GetResult();

        var types = new[] { typeof(Article), typeof(Category), typeof(Tag), typeof(Struo.Infrastructure.Files.File) };
        var collections = MetadataScanner.ScanTypes(types);
        var provider = new CachedMetadataProvider(collections);
        var registry = new EntityRegistry(MetadataScanner.ScanDescriptors(types));
        var collectionTypes = new Dictionary<string, Type>(StringComparer.OrdinalIgnoreCase)
        {
            ["article"] = typeof(Article), ["category"] = typeof(Category),
            ["tag"] = typeof(Tag), ["file"] = typeof(Struo.Infrastructure.Files.File),
        };
        var graph = new RelationshipGraph(collections, collectionTypes);
        var repo = new SqlSugarItemRepository(db, registry, graph, provider, new StruoQueryOptions());
        var resolver = new RelationFilterResolver(repo, graph, provider, registry, new StruoQueryOptions());
        var expander = new RelationExpander(repo, graph, resolver, new StruoQueryOptions());
        var languages = new LanguageProvider(db);
        _svc = new ItemService(repo, provider, registry, new AllowAllPermissionService(),
            graph, expander, graph, resolver, languages, new StruoQueryOptions(), new GanssHtmlSanitizer(),
            new TestCurrentUserAccessor(Guid.Empty));
    }

    public void Dispose() => _file.Dispose();

    [Fact]
    public async Task Create_strips_script_from_translatable_richtext_body()
    {
        using var body = System.Text.Json.JsonDocument.Parse(
            """{"status":"draft","translations":{"en":{"title":"T","body":"<p>ok</p><script>alert(1)</script><p onclick=\"x()\">y</p>"}}}""");
        var created = await _svc.CreateAsync("article", body.RootElement);
        var id = created["id"]!.ToString()!;

        var reloaded = await _svc.GetAsync("article", id, locale: "en");
        var translations = (IReadOnlyDictionary<string, Dictionary<string, object?>>)reloaded!["translations"]!;
        var en = translations["en"];
        var stored = en["body"] as string;
        stored.Should().NotBeNull();
        stored!.Should().Contain("ok").And.NotContain("script").And.NotContain("onclick");
    }

    [Fact]
    public async Task Create_coerces_blank_richtext_to_null()
    {
        using var body = System.Text.Json.JsonDocument.Parse(
            """{"status":"draft","translations":{"en":{"title":"T","body":"<p></p>"}}}""");
        var created = await _svc.CreateAsync("article", body.RootElement);
        var id = created["id"]!.ToString()!;

        var reloaded = await _svc.GetAsync("article", id, locale: "en");
        var translations = (IReadOnlyDictionary<string, Dictionary<string, object?>>)reloaded!["translations"]!;
        var en = translations["en"];
        en["body"].Should().BeNull();
    }
}
