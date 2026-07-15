// tests/Struo.Tests/Revisions/RevisionServiceTests.cs
using System.Text.Json;
using Struo.Application.Configuration;
using Struo.Application.Query;
using Struo.Application.Revisions;
using Struo.Domain.Query;
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
/// Wires a real <see cref="ItemService"/> + <see cref="SqlSugarRevisionStore"/> +
/// <see cref="RevisionSnapshotBuilder"/> over the sample Blog types, mirroring
/// <c>SnapshotBuilderHarness</c>'s construction (Task 4). Article is revisioned
/// (<c>Revisions = true</c>); Category is not, so it exercises the <c>meta.Revisions</c> gate.
/// <para>
/// When <paramref name="FailCapture"/>-equivalent (<see cref="Create"/>'s <c>failCapture</c> flag)
/// is set, the <see cref="ItemService"/> is wired with a stub <see cref="IRevisionStore"/> whose
/// <see cref="IRevisionStore.CaptureAsync"/> always throws, so the third test can prove the
/// capture sits inside the same write transaction as the parent row.
/// </para>
/// </summary>
public sealed class RevisionServiceHarness : IDisposable
{
    private readonly SqliteTestDatabase _file = new();

    public IItemRepository Repository { get; }
    public ItemService Service { get; }

    /// <summary>The real revision store — always backed by the same DB the write path uses, even
    /// when <see cref="Service"/> itself was wired with a throwing stub (Store is unused by the
    /// roll-back test; it exists so future tests can inspect committed revisions directly).</summary>
    public IRevisionStore Store { get; }

    private RevisionServiceHarness(bool failCapture)
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
        var realStore = new SqlSugarRevisionStore(db, revisionUser);
        IRevisionStore serviceStore = failCapture ? new ThrowingRevisionStore() : realStore;
        var snapshotBuilder = new RevisionSnapshotBuilder(repo, provider, registry, graph);

        Repository = repo;
        Store = realStore;
        Service = new ItemService(repo, provider, registry, new AllowAllPermissionService(),
            graph, expander, graph, resolver, languages, new StruoQueryOptions(), new GanssHtmlSanitizer(),
            revisionUser, serviceStore, snapshotBuilder);
    }

    public static RevisionServiceHarness Create(bool failCapture = false) => new(failCapture);

    public void Dispose() => _file.Dispose();

    private static JsonElement BodyOf(object obj)
    {
        using var doc = JsonDocument.Parse(JsonSerializer.Serialize(obj));
        return doc.RootElement.Clone();
    }

    /// <summary>A partial update body (e.g. <c>{"status":"published"}</c>).</summary>
    public JsonElement Body(string status) => BodyOf(new { status });

    /// <summary>The create-article call as an un-awaited Task, so a failing capture's exception can
    /// be observed via <c>Assert.ThrowsAnyAsync</c> around the whole write.</summary>
    public Task<IReadOnlyDictionary<string, object?>> CreateArticleTask(string status) =>
        Service.CreateAsync("article", BodyOf(new
        {
            status,
            translations = new Dictionary<string, object> { ["en"] = new { title = "T" } }
        }));

    public async Task<string> CreateArticleAsync(string status)
    {
        var created = await CreateArticleTask(status);
        return created["id"]!.ToString()!;
    }

    public async Task<string> CreateCategoryAsync(string name)
    {
        var created = await Service.CreateAsync("category", BodyOf(new { name }));
        return created["id"]!.ToString()!;
    }

    /// <summary>Simulates a capture-time failure (e.g. a storage error) so the roll-back test can
    /// prove the capture runs inside the same <c>InTransactionAsync</c> as the parent write.</summary>
    private sealed class ThrowingRevisionStore : IRevisionStore
    {
        public Task CaptureAsync(string collection, string itemId, string operation, string snapshotJson, CancellationToken ct = default) =>
            throw new InvalidOperationException("Simulated revision capture failure (test).");
        public Task<IReadOnlyList<RevisionInfo>> ListAsync(string collection, string itemId, CancellationToken ct = default) =>
            throw new NotImplementedException("Not expected to be called in the roll-back test.");
        public Task<RevisionRecord?> GetAsync(string collection, string itemId, long revisionNumber, CancellationToken ct = default) =>
            throw new NotImplementedException("Not expected to be called in the roll-back test.");
    }
}

public sealed class RevisionServiceTests
{
    [Fact]
    public async Task Create_then_update_appends_two_revisions()
    {
        using var h = RevisionServiceHarness.Create();
        var id = await h.CreateArticleAsync(status: "draft");        // create -> rev 1
        await h.Service.UpdateAsync("article", id, h.Body(status: "published"), default); // update -> rev 2

        var list = await h.Store.ListAsync("article", id, default);
        Assert.Equal(2, list.Count);
        Assert.Equal("update", list[0].Operation);                   // newest-first
        Assert.Equal("create", list[1].Operation);
    }

    [Fact]
    public async Task Non_revisioned_collection_captures_nothing()
    {
        using var h = RevisionServiceHarness.Create();
        var id = await h.CreateCategoryAsync(name: "Cat");           // Category is NOT revisioned
        var list = await h.Store.ListAsync("category", id, default);
        Assert.Empty(list);
    }

    [Fact]
    public async Task Capture_failure_rolls_back_the_write()
    {
        using var h = RevisionServiceHarness.Create(failCapture: true); // store stub throws in CaptureAsync
        await Assert.ThrowsAnyAsync<Exception>(() => h.CreateArticleTask(status: "draft"));
        // the parent write rolled back with the failed capture (same transaction) -> no article persisted
        var page = await h.Service.QueryAsync("article", new QueryModel(null, null, [], 100, 0, null), null, default);
        Assert.Empty(page.Data);
    }
}
