// tests/Struo.Tests/Revisions/RevisionSnapshotBuilderTests.cs
using Struo.Tests.Support.Seeding;
using System.Text.Json;
using Struo.Application.Configuration;
using Struo.Application.Query;
using Struo.Infrastructure.Metadata;
using Struo.Infrastructure.Persistence;
using Struo.Infrastructure.Query;
using Struo.Infrastructure.Localization;
using Struo.Infrastructure.Revisions;
using Struo.Infrastructure.Security;
using Struo.Tests.Fixtures;
using Struo.Tests.Support;
using Xunit;

namespace Struo.Tests.Revisions;

/// <summary>
/// Wires an <see cref="ItemService"/> + <see cref="RevisionSnapshotBuilder"/> over the fixture types
/// (FxArticle/FxCategory/FxTag/File), mirroring <c>ItemServiceTests</c>' harness construction, so a live item
/// with an M2O relation, an M2M relation, a multi-value own-field, and i18n translations can be seeded
/// through the normal write path and then snapshotted.
/// </summary>
public sealed class SnapshotBuilderHarness : IDisposable
{
    private readonly ITestDatabase _database = TestBackend.CreateUnitDatabase();

    public IItemRepository Repository { get; }
    public ItemService Service { get; }
    public RevisionSnapshotBuilder Builder { get; }

    private SnapshotBuilderHarness()
    {
        var db = SqlSugarClientFactory.Create(
            _database.Options(),
            new TestCurrentUserAccessor(Guid.Empty));
        db.CodeFirst.InitTables<FxArticle>();
        db.CodeFirst.InitTables<FxArticleTranslation>();
        db.CodeFirst.InitTables<Language>();
        db.CodeFirst.InitTables<FxTag>();
        db.CodeFirst.InitTables<FxArticleTag>();
        db.CodeFirst.InitTables<FxCategory>();
        db.CodeFirst.InitTables<Revision>();
        db.CodeFirst.InitTables<Struo.Infrastructure.Files.MediaFolder>();
        LanguageSeeder.SeedAsync(db, TestLocalization.Default).GetAwaiter().GetResult();

        var types = new[]
        {
            typeof(FxArticle), typeof(FxCategory), typeof(FxTag), typeof(Struo.Infrastructure.Files.File),
            typeof(Struo.Infrastructure.Files.MediaFolder),
        };
        var collections = MetadataScanner.ScanTypes(types);
        var provider = new CachedMetadataProvider(collections);
        var registry = new EntityRegistry(MetadataScanner.ScanDescriptors(types));
        var collectionTypes = new Dictionary<string, Type>(StringComparer.OrdinalIgnoreCase)
        {
            ["fxArticle"]  = typeof(FxArticle),
            ["fxCategory"] = typeof(FxCategory),
            ["fxTag"]      = typeof(FxTag),
            ["file"]     = typeof(Struo.Infrastructure.Files.File),
            ["mediafolder"] = typeof(Struo.Infrastructure.Files.MediaFolder),
        };
        var graph = new RelationshipGraph(collections, collectionTypes);
        var repo = new SqlSugarItemRepository(db, registry, graph, provider, new StruoQueryOptions());
        var expander = new RelationExpander(repo, graph, new StruoQueryOptions());
        var languages = new LanguageProvider(db);

        var revisionUser = new TestCurrentUserAccessor(Guid.Empty);
        var revisionStore = new SqlSugarRevisionStore(db, revisionUser);
        Repository = repo;
        Builder = new RevisionSnapshotBuilder(repo, provider, registry, graph);
        Service = TestItemService.Create(
            repo,
            provider,
            registry,
            graph,
            expander,
            graph,
            languages,
            revisionUser,
            revisionStore,
            Builder);
    }

    public static SnapshotBuilderHarness Create() => new();

    public void Dispose() => _database.Dispose();

    private static JsonElement Body(object obj)
    {
        using var doc = JsonDocument.Parse(JsonSerializer.Serialize(obj));
        return doc.RootElement.Clone();
    }

    private static readonly string[] ApacOnly = ["apac"];

    /// <summary>Creates a category + tag, then an article referencing both (M2O + M2M), with a
    /// multi-value own-field and en/zh-TW translations, via <see cref="ItemService.CreateAsync"/>.</summary>
    public async Task<(Guid articleId, Guid categoryId, Guid tagId)> SeedArticleWithRelationsAndI18nAsync()
    {
        var category = await Service.CreateAsync("fxCategory", Body(new { name = "Tech" }));
        var categoryId = (Guid)category["id"]!;

        var tag = await Service.CreateAsync("fxTag", Body(new { name = "AI" }));
        var tagId = (Guid)tag["id"]!;

        var article = await Service.CreateAsync("fxArticle", Body(new
        {
            status = "draft",
            categoryId,
            tags = new[] { tagId },
            regions = ApacOnly,
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
        var entity = await h.Repository.GetByIdAsync("fxArticle", articleId.ToString(), default);

        var json = await h.Builder.BuildAsync("fxArticle", entity!, default);
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
