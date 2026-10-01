// tests/Struo.Tests/Query/ItemServiceMalformedBodyTests.cs
using System.Text.Json;
using AwesomeAssertions;
using Struo.Application.Configuration;
using Struo.Application.Query;
using Struo.Domain.Query;
using Struo.Infrastructure.Localization;
using Struo.Infrastructure.Metadata;
using Struo.Infrastructure.Persistence;
using Struo.Infrastructure.Query;
using Struo.Infrastructure.Revisions;
using Struo.Infrastructure.Security;
using Struo.Sample.Blog;
using Struo.Tests.Support;
using Xunit;

namespace Struo.Tests.Query;

// A handful of malformed write-path request bodies fell through to an
// unhandled InvalidOperationException/FormatException (-> 500) instead of the established
// QueryException (-> 400) client-error path. This harness mirrors ItemServiceTests' sample-domain
// wiring (Article/Category/Tag, M2M via "tags") and additionally scans Language so the "language"
// collection's code-format guard (Site 3) can be exercised directly.
internal sealed class MalformedBodyHarness : IDisposable
{
    private readonly SqliteTestDatabase _file = new();

    public ItemService Service { get; }

    private MalformedBodyHarness()
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
        LanguageSeeder.SeedAsync(db, TestLocalization.Default).GetAwaiter().GetResult();

        var types = new[]
        {
            typeof(Article), typeof(Category), typeof(Tag),
            typeof(Struo.Infrastructure.Files.File), typeof(Language),
            typeof(Struo.Infrastructure.Files.MediaFolder),
        };
        var collections = MetadataScanner.ScanTypes(types);
        var provider = new CachedMetadataProvider(collections);
        var registry = new EntityRegistry(MetadataScanner.ScanDescriptors(types));
        var collectionTypes = new Dictionary<string, Type>(StringComparer.OrdinalIgnoreCase)
        {
            ["article"]  = typeof(Article),
            ["category"] = typeof(Category),
            ["tag"]      = typeof(Tag),
            ["file"]     = typeof(Struo.Infrastructure.Files.File),
            ["language"] = typeof(Language),
            ["mediafolder"] = typeof(Struo.Infrastructure.Files.MediaFolder),
        };
        var graph = new RelationshipGraph(collections, collectionTypes);
        var repo = new SqlSugarItemRepository(db, registry, graph, provider, new StruoQueryOptions());
        var expander = new RelationExpander(repo, graph, new StruoQueryOptions());
        var languages = new LanguageProvider(db);
        var revisionUser = new TestCurrentUserAccessor(Guid.Empty);
        var revisionStore = new SqlSugarRevisionStore(db, revisionUser);
        var snapshotBuilder = new RevisionSnapshotBuilder(repo, provider, registry, graph);
        Service = new ItemService(repo, provider, registry, new AllowAllPermissionService(),
            graph, expander, graph, languages, new StruoQueryOptions(), new GanssHtmlSanitizer(),
            revisionUser, revisionStore, snapshotBuilder, new NoopUserSessionRevocationService());
    }

    public static MalformedBodyHarness Create() => new();

    public void Dispose() => _file.Dispose();

    public static JsonElement BodyOf(string json)
    {
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.Clone();
    }

    public async Task<string> CreateArticleAsync(Guid[]? tags = null)
    {
        var tagsJson = tags is null ? "" : $",\"tags\":[{string.Join(",", tags.Select(t => $"\"{t}\""))}]";
        var body = BodyOf(
            "{\"status\":\"draft\",\"translations\":{\"en\":{\"title\":\"T\"}}" + tagsJson + "}");
        var created = await Service.CreateAsync("article", body);
        return created["id"]!.ToString()!;
    }

    public Task<string> CreateTagAsync(string name) =>
        Service.CreateAsync("tag", BodyOf("{\"name\":\"" + name + "\"}"))
            .ContinueWith(t => t.Result["id"]!.ToString()!);
}

/// <summary>
/// Non-object top-level request bodies (array / scalar) must 400 (<see cref="QueryException"/>),
/// not 500, on both Create and Update, including for the "language" collection, whose code-format
/// guard runs only after this non-object check, never touching a malformed body. Real
/// SQLite-backed <see cref="ItemService"/>, no stubs.
/// </summary>
public sealed class ItemServiceMalformedBodyTests
{
    // ── Site 1: CreateAsync with a non-object top-level body ───────────────────────────────────

    [Fact]
    public async Task Create_with_array_body_throws_query_exception()
    {
        using var h = MalformedBodyHarness.Create();
        var body = MalformedBodyHarness.BodyOf("[1,2]");

        await Assert.ThrowsAsync<QueryException>(() => h.Service.CreateAsync("article", body));
    }

    [Fact]
    public async Task Create_with_scalar_string_body_throws_query_exception()
    {
        using var h = MalformedBodyHarness.Create();
        var body = MalformedBodyHarness.BodyOf("\"x\"");

        await Assert.ThrowsAsync<QueryException>(() => h.Service.CreateAsync("article", body));
    }

    // ── Site 3: the "language" collection's own guard must not 500 on a malformed body either ──

    [Fact]
    public async Task Create_language_with_array_body_throws_query_exception()
    {
        using var h = MalformedBodyHarness.Create();
        var body = MalformedBodyHarness.BodyOf("[1,2]");

        await Assert.ThrowsAsync<QueryException>(() => h.Service.CreateAsync("language", body));
    }

    // ── Site 1/3: UpdateAsync (via UpdateCoreAsync) with a non-object top-level body ────────────

    [Fact]
    public async Task Update_with_non_object_body_throws_query_exception()
    {
        using var h = MalformedBodyHarness.Create();
        var articleId = await h.CreateArticleAsync();
        var body = MalformedBodyHarness.BodyOf("5");

        await Assert.ThrowsAsync<QueryException>(() => h.Service.UpdateAsync("article", articleId, body));
    }

    [Fact]
    public async Task Update_language_with_non_object_body_throws_query_exception()
    {
        using var h = MalformedBodyHarness.Create();
        var body = MalformedBodyHarness.BodyOf("[1,2]");

        // Any id is fine — the guard fires before the row lookup.
        await Assert.ThrowsAsync<QueryException>(
            () => h.Service.UpdateAsync("language", Guid.NewGuid().ToString(), body));
    }

    // ── Site 2: M2M array element of the wrong JSON type must 400, not throw Format/InvalidOperation ─

    [Fact]
    public async Task Update_m2m_array_with_non_integer_number_element_throws_query_exception()
    {
        using var h = MalformedBodyHarness.Create();
        var articleId = await h.CreateArticleAsync();
        var body = MalformedBodyHarness.BodyOf("{\"tags\":[1.5]}");

        await Assert.ThrowsAsync<QueryException>(() => h.Service.UpdateAsync("article", articleId, body));
    }

    [Fact]
    public async Task Update_m2m_array_with_boolean_element_throws_query_exception()
    {
        using var h = MalformedBodyHarness.Create();
        var articleId = await h.CreateArticleAsync();
        var body = MalformedBodyHarness.BodyOf("{\"tags\":[true]}");

        await Assert.ThrowsAsync<QueryException>(() => h.Service.UpdateAsync("article", articleId, body));
    }

    // ── Regression: a valid object body with a valid M2M id array still succeeds ────────────────

    [Fact]
    public async Task Update_m2m_array_with_valid_ids_still_succeeds()
    {
        using var h = MalformedBodyHarness.Create();
        var tagId = Guid.Parse(await h.CreateTagAsync("T1"));
        var articleId = await h.CreateArticleAsync();
        var body = MalformedBodyHarness.BodyOf("{\"tags\":[\"" + tagId + "\"]}");

        var updated = await h.Service.UpdateAsync("article", articleId, body);

        updated.Should().NotBeNull();
    }
}
