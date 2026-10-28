// tests/Struo.Tests/Query/ItemServiceTests.cs
using Struo.Tests.Support.Seeding;
using AwesomeAssertions;
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

namespace Struo.Tests.Query;

public sealed class ItemServiceTests : IDisposable
{
    private readonly ITestDatabase _file = TestBackend.CreateUnitDatabase();
    private readonly ItemService _svc;

    public ItemServiceTests()
    {
        var db = SqlSugarClientFactory.Create(
            _file.Options(),
            new TestCurrentUserAccessor(Guid.Empty));
        db.CodeFirst.InitTables<FxArticle>();
        db.CodeFirst.InitTables<FxArticleTranslation>();
        db.CodeFirst.InitTables<Language>();
        db.CodeFirst.InitTables<FxTag>();
        db.CodeFirst.InitTables<FxArticleTag>();
        db.CodeFirst.InitTables<FxCategory>();
        db.CodeFirst.InitTables<Revision>();
        LanguageSeeder.SeedAsync(db, TestLocalization.Default).GetAwaiter().GetResult();

        var collections = MetadataScanner.ScanTypes(
            [typeof(FxArticle), typeof(FxCategory), typeof(FxTag), typeof(Struo.Infrastructure.Files.File),
             typeof(Struo.Infrastructure.Files.MediaFolder)]);
        var provider = new CachedMetadataProvider(collections);
        var registry = new EntityRegistry(MetadataScanner.ScanDescriptors(
            [typeof(FxArticle), typeof(FxCategory), typeof(FxTag), typeof(Struo.Infrastructure.Files.File),
             typeof(Struo.Infrastructure.Files.MediaFolder)]));
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
        var snapshotBuilder = new RevisionSnapshotBuilder(repo, provider, registry, graph);
        _svc = TestItemService.Create(
            repo,
            provider,
            registry,
            graph,
            expander,
            graph,
            languages,
            revisionUser,
            revisionStore,
            snapshotBuilder);
    }

    public void Dispose() => _file.Dispose();

    [Fact]
    public async Task Create_projects_id_audit_and_camelCase_fields()
    {
        using var body = System.Text.Json.JsonDocument.Parse(
            """{"status":"draft","translations":{"en":{"title":"Hello"}}}""");
        var dict = await _svc.CreateAsync("fxArticle", body.RootElement);

        dict.Should().ContainKey("id");
        dict["status"].Should().Be("draft");
        dict.Should().ContainKey("createdAt");
        // seoTitle is now a translatable field on ArticleTranslation
        dict.Should().NotContainKey("seoTitle");
    }

    [Fact]
    public async Task Update_persists_m2o_foreign_key()
    {
        using var catABody = System.Text.Json.JsonDocument.Parse("""{"name":"Category A"}""");
        var catA = await _svc.CreateAsync("fxCategory", catABody.RootElement);
        var catAId = (Guid)catA["id"]!;

        using var catBBody = System.Text.Json.JsonDocument.Parse("""{"name":"Category B"}""");
        var catB = await _svc.CreateAsync("fxCategory", catBBody.RootElement);
        var catBId = (Guid)catB["id"]!;

        using var createBody = System.Text.Json.JsonDocument.Parse(
            "{\"status\":\"draft\",\"categoryId\":\"" + catAId + "\",\"translations\":{\"en\":{\"title\":\"Hello\"}}}");
        var created = await _svc.CreateAsync("fxArticle", createBody.RootElement);
        var articleId = created["id"]!.ToString()!;

        using var updateBody = System.Text.Json.JsonDocument.Parse(
            "{\"categoryId\":\"" + catBId + "\"}");
        var updated = await _svc.UpdateAsync("fxArticle", articleId, updateBody.RootElement);
        updated.Should().NotBeNull();

        var deep = new Struo.Domain.Query.DeepSpec(
            new Dictionary<string, Struo.Domain.Query.DeepRelationSpec>(StringComparer.OrdinalIgnoreCase)
            {
                ["category"] = new Struo.Domain.Query.DeepRelationSpec(null, null)
            });
        var reloaded = await _svc.GetAsync("fxArticle", articleId, deep);
        reloaded.Should().NotBeNull();
        reloaded!.Should().ContainKey("category");
        var category = (IReadOnlyDictionary<string, object?>)reloaded["category"]!;
        category["id"].Should().Be(catBId);
    }

    [Fact]
    public async Task Query_filters_by_m2o_relation_foreign_key()
    {
        using var catABody = System.Text.Json.JsonDocument.Parse("""{"name":"Category A"}""");
        var catA = await _svc.CreateAsync("fxCategory", catABody.RootElement);
        var catAId = (Guid)catA["id"]!;

        using var catBBody = System.Text.Json.JsonDocument.Parse("""{"name":"Category B"}""");
        var catB = await _svc.CreateAsync("fxCategory", catBBody.RootElement);
        var catBId = (Guid)catB["id"]!;

        using var articleABody = System.Text.Json.JsonDocument.Parse(
            "{\"status\":\"draft\",\"categoryId\":\"" + catAId + "\",\"translations\":{\"en\":{\"title\":\"A\"}}}");
        await _svc.CreateAsync("fxArticle", articleABody.RootElement);

        using var articleBBody = System.Text.Json.JsonDocument.Parse(
            "{\"status\":\"draft\",\"categoryId\":\"" + catBId + "\",\"translations\":{\"en\":{\"title\":\"B\"}}}");
        await _svc.CreateAsync("fxArticle", articleBBody.RootElement);

        var filter = new Struo.Domain.Query.ComparisonFilter(
            "categoryId", Struo.Domain.Query.QueryOperator.Eq, catAId.ToString());
        var query = new Struo.Domain.Query.QueryModel(null, filter, [], 0, 0, null);
        var result = await _svc.QueryAsync("fxArticle", query);

        result.Data.Should().HaveCount(1);
        result.Data[0].Should().NotContainKey("categoryId"); // FK is not projected (meta.Fields unchanged)
        var deep = new Struo.Domain.Query.DeepSpec(
            new Dictionary<string, Struo.Domain.Query.DeepRelationSpec>(StringComparer.OrdinalIgnoreCase)
            {
                ["category"] = new Struo.Domain.Query.DeepRelationSpec(null, null)
            });
        var reloaded = await _svc.GetAsync("fxArticle", result.Data[0]["id"]!.ToString()!, deep);
        var category = (IReadOnlyDictionary<string, object?>)reloaded!["category"]!;
        category["id"].Should().Be(catAId);
    }
}
