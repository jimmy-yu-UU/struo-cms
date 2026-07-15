// tests/Struo.Tests/Revisions/RevisionSnapshotBuilderTests.cs
using System.Text.Json;
using Struo.Application.Configuration;
using Struo.Application.Query;
using Struo.Infrastructure.Metadata;
using Struo.Infrastructure.Persistence;
using Struo.Infrastructure.Query;
using Struo.Infrastructure.Localization;
using Struo.Infrastructure.Revisions;
using Struo.Infrastructure.Security;
using Struo.Sample.Blog;
using Struo.Tests.Support;
using Xunit;

namespace Struo.Tests.Revisions;

/// <summary>
/// Wires an <see cref="ItemService"/> + <see cref="RevisionSnapshotBuilder"/> over the sample Blog types
/// (Article/Category/Tag/File), mirroring <c>ItemServiceTests</c>' harness construction, so a live item
/// with an M2O relation, an M2M relation, a multi-value own-field, and i18n translations can be seeded
/// through the normal write path and then snapshotted.
/// </summary>
public sealed class SnapshotBuilderHarness : IDisposable
{
    private readonly SqliteTestDatabase _file = new();

    public IItemRepository Repository { get; }
    public ItemService Service { get; }
    public RevisionSnapshotBuilder Builder { get; }

    private SnapshotBuilderHarness()
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
        db.CodeFirst.InitTables<Revision>();
        LanguageSeeder.SeedAsync(db).GetAwaiter().GetResult();

        var types = new[] { typeof(Article), typeof(Category), typeof(Tag), typeof(Struo.Infrastructure.Files.File) };
        var collections = MetadataScanner.ScanTypes(types);
        var provider = new CachedMetadataProvider(collections);
        var registry = new EntityRegistry(MetadataScanner.ScanDescriptors(types));
        var collectionTypes = new Dictionary<string, Type>(StringComparer.OrdinalIgnoreCase)
        {
            ["article"]  = typeof(Article),
            ["category"] = typeof(Category),
            ["tag"]      = typeof(Tag),
            ["file"]     = typeof(Struo.Infrastructure.Files.File),
        };
        var graph = new RelationshipGraph(collections, collectionTypes);
        var repo = new SqlSugarItemRepository(db, registry, graph, provider, new StruoQueryOptions());
        var resolver = new RelationFilterResolver(repo, graph, provider, registry, new StruoQueryOptions());
        var expander = new RelationExpander(repo, graph, resolver, new StruoQueryOptions());
        var languages = new LanguageProvider(db);

        var revisionUser = new TestCurrentUserAccessor(Guid.Empty);
        var revisionStore = new SqlSugarRevisionStore(db, revisionUser);
        Repository = repo;
        Builder = new RevisionSnapshotBuilder(repo, provider, registry, graph);
        Service = new ItemService(repo, provider, registry, new AllowAllPermissionService(),
            graph, expander, graph, resolver, languages, new StruoQueryOptions(), new GanssHtmlSanitizer(),
            revisionUser, revisionStore, Builder);
    }

    public static SnapshotBuilderHarness Create() => new();

    public void Dispose() => _file.Dispose();

    private static JsonElement Body(object obj)
    {
        using var doc = JsonDocument.Parse(JsonSerializer.Serialize(obj));
        return doc.RootElement.Clone();
    }

    /// <summary>Creates a category + tag, then an article referencing both (M2O + M2M), with a
    /// multi-value own-field and en/zh-TW translations, via <see cref="ItemService.CreateAsync"/>.</summary>
    public async Task<(Guid articleId, Guid categoryId, Guid tagId)> SeedArticleWithRelationsAndI18nAsync()
    {
        var category = await Service.CreateAsync("category", Body(new { name = "Tech" }));
        var categoryId = (Guid)category["id"]!;

        var tag = await Service.CreateAsync("tag", Body(new { name = "AI" }));
        var tagId = (Guid)tag["id"]!;

        var article = await Service.CreateAsync("article", Body(new
        {
            status = "draft",
            categoryId,
            tags = new[] { tagId },
            regions = new[] { "apac" },
            translations = new Dictionary<string, object>
            {
                ["en"] = new { title = "AI" },
                ["zh-TW"] = new { title = "人工智慧" },
            }
        }));
        var articleId = (Guid)article["id"]!;

        return (articleId, categoryId, tagId);
    }
}

public sealed class RevisionSnapshotBuilderTests
{
    [Fact]
    public async Task Snapshot_is_revert_capable()
    {
        using var h = SnapshotBuilderHarness.Create();
        var (articleId, categoryId, tagId) = await h.SeedArticleWithRelationsAndI18nAsync();
        var entity = await h.Repository.GetByIdAsync("article", articleId.ToString(), default);

        var json = await h.Builder.BuildAsync("article", entity!, default);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        // M2O FK id present under camel FK name (NOT in the read projection).
        Assert.Equal(categoryId.ToString(), root.GetProperty("categoryId").GetString());
        // M2M relation as an ordered id array under the relation name.
        Assert.Equal(tagId.ToString(), root.GetProperty("tags")[0].GetString());
        // Multi-value own-field preserved.
        Assert.Contains("apac", root.GetProperty("regions").EnumerateArray().Select(e => e.GetString()));
        // All-locale translations, raw values, verbatim locale keys.
        Assert.True(root.GetProperty("translations").TryGetProperty("zh-TW", out var zh));
        Assert.Equal("人工智慧", zh.GetProperty("title").GetString());   // CJK code-point-exact
        // version captured for reference.
        Assert.True(root.TryGetProperty("version", out _));
    }
}
