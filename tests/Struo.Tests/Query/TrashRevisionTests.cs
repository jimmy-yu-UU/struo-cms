// tests/Struo.Tests/Query/TrashRevisionTests.cs
using System.Text.Json;
using SqlSugar;
using Struo.Application.Configuration;
using Struo.Application.Localization;
using Struo.Application.Query;
using Struo.Application.Revisions;
using Struo.Domain.Auditing;
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

/// <summary>
/// Trashing (soft-delete) and restoring an item must (a) increment the optimistic-lock
/// <see cref="AuditableEntity.Version"/> in the SAME UPDATE — so a stale client 409s after a
/// restore — and (b) capture a revision (operation "delete"/"restore") when the collection has
/// <c>Revisions=true</c>, all inside one rollback-safe transaction. Real SQLite-backed
/// <see cref="ItemService"/>, no stubs — mirrors <c>PurgeIntegrityHarness</c>'s construction, with a
/// throwing-on-capture revision-store variant for the rollback-atomicity case.
/// </summary>
public sealed class TrashRevisionTests
{
    // ── (a) soft-delete an Article: Version +1, one "delete" revision with a live snapshot ────────

    [Fact]
    public async Task Soft_delete_article_bumps_version_and_records_delete_revision()
    {
        using var h = TrashRevisionHarness.Create();
        var articleId = await h.CreateArticleAsync(status: "published");

        var before = (Article)(await h.Repository.GetByIdAsync("article", articleId, DeletedFilter.With))!;
        var versionBefore = before.Version;
        var revsBefore = (await h.RevisionStore.ListAsync("article", articleId)).Count; // 1 = "create"

        var deleted = await h.Service.DeleteAsync("article", articleId, purge: false);

        Assert.True(deleted);
        var trashed = (Article)(await h.Repository.GetByIdAsync("article", articleId, DeletedFilter.With))!;
        Assert.NotNull(trashed.DeletedAt);
        Assert.Equal(versionBefore + 1, trashed.Version);

        var revs = await h.RevisionStore.ListAsync("article", articleId);
        Assert.Equal(revsBefore + 1, revs.Count);
        Assert.Equal("delete", revs[0].Operation); // newest-first

        var rec = await h.RevisionStore.GetAsync("article", articleId, revs[0].RevisionNumber);
        Assert.NotNull(rec);
        using var snap = JsonDocument.Parse(rec!.Snapshot);
        Assert.Equal("published", snap.RootElement.GetProperty("status").GetString()); // deletion-time field value
    }

    // ── (b) restore the trashed Article: Version +1 again, one "restore" revision ─────────────────

    [Fact]
    public async Task Restore_article_bumps_version_again_and_records_restore_revision()
    {
        using var h = TrashRevisionHarness.Create();
        var articleId = await h.CreateArticleAsync(status: "draft");
        await h.Service.DeleteAsync("article", articleId, purge: false); // trash (version -> 1, "delete")

        var afterTrash = (Article)(await h.Repository.GetByIdAsync("article", articleId, DeletedFilter.With))!;
        var versionAfterTrash = afterTrash.Version;

        var restored = await h.Service.RestoreAsync("article", articleId);

        Assert.NotNull(restored);
        var live = (Article)(await h.Repository.GetByIdAsync("article", articleId, DeletedFilter.Exclude))!;
        Assert.Null(live.DeletedAt);
        Assert.Equal(versionAfterTrash + 1, live.Version);

        var revs = await h.RevisionStore.ListAsync("article", articleId);
        Assert.Equal("restore", revs[0].Operation); // newest-first
        Assert.Equal(1, revs.Count(r => r.Operation == "restore"));
        Assert.Equal(1, revs.Count(r => r.Operation == "delete"));
    }

    // ── (c) Category is soft-deletable + AuditableEntity but NOT revisioned ───────────────────────
    //        Version still +1; zero revisions captured.

    [Fact]
    public async Task Soft_delete_category_bumps_version_but_records_no_revision()
    {
        using var h = TrashRevisionHarness.Create();
        var categoryId = await h.CreateCategoryAsync("Cat");

        var before = (Category)(await h.Repository.GetByIdAsync("category", categoryId, DeletedFilter.With))!;
        var versionBefore = before.Version;

        var deleted = await h.Service.DeleteAsync("category", categoryId, purge: false);

        Assert.True(deleted);
        var trashed = (Category)(await h.Repository.GetByIdAsync("category", categoryId, DeletedFilter.With))!;
        Assert.NotNull(trashed.DeletedAt);
        Assert.Equal(versionBefore + 1, trashed.Version);

        // Category has no [CmsCollection(Revisions=true)] -> no revision history at all.
        Assert.Empty(await h.RevisionStore.ListAsync("category", categoryId));
    }

    // ── (c') Non-AuditableEntity ISoftDeletable (CascadeNode) keeps current behavior: no Version to ─
    //         bump, but a revision IS still captured because CascadeNode is revisioned.

    [Fact]
    public async Task Soft_delete_non_auditable_revisioned_node_records_revision_without_version()
    {
        using var h = TrashRevisionHarness.Create();
        var nodeId = await h.CreateCascadeNodeAsync("N1");
        var revsBefore = (await h.RevisionStore.ListAsync("cascadeNode", nodeId)).Count; // 1 = "create"

        var deleted = await h.Service.DeleteAsync("cascadeNode", nodeId, purge: false);

        Assert.True(deleted);
        var trashed = (CascadeNode)(await h.Repository.GetByIdAsync("cascadeNode", nodeId, DeletedFilter.With))!;
        Assert.NotNull(trashed.DeletedAt);

        var revs = await h.RevisionStore.ListAsync("cascadeNode", nodeId);
        Assert.Equal(revsBefore + 1, revs.Count);
        Assert.Equal("delete", revs[0].Operation);
    }

    // ── (f) re-trashing an already-trashed row is a no-op: no re-stamp, no version bump, no dup revision ─

    [Fact]
    public async Task Soft_delete_already_trashed_article_is_a_noop()
    {
        using var h = TrashRevisionHarness.Create();
        var articleId = await h.CreateArticleAsync(status: "published");
        Assert.True(await h.Service.DeleteAsync("article", articleId, purge: false)); // first trash

        var afterFirst = (Article)(await h.Repository.GetByIdAsync("article", articleId, DeletedFilter.With))!;
        var versionAfterFirst = afterFirst.Version;
        var deleteRevsAfterFirst = (await h.RevisionStore.ListAsync("article", articleId))
            .Count(r => r.Operation == "delete");
        Assert.Equal(1, deleteRevsAfterFirst);

        // Second DELETE on the already-trashed row still reports success (unchanged semantics)…
        Assert.True(await h.Service.DeleteAsync("article", articleId, purge: false));

        // …but must NOT re-stamp / bump Version / append another "delete" revision.
        var afterSecond = (Article)(await h.Repository.GetByIdAsync("article", articleId, DeletedFilter.With))!;
        Assert.Equal(versionAfterFirst, afterSecond.Version);
        Assert.Equal(1, (await h.RevisionStore.ListAsync("article", articleId)).Count(r => r.Operation == "delete"));
    }

    // ── (g) restoring an already-live row is a true no-op: no version bump, no "restore" revision ──

    [Fact]
    public async Task Restore_already_live_article_is_a_noop()
    {
        using var h = TrashRevisionHarness.Create();
        var articleId = await h.CreateArticleAsync(status: "published"); // never trashed

        var before = (Article)(await h.Repository.GetByIdAsync("article", articleId, DeletedFilter.With))!;
        var versionBefore = before.Version;

        var restored = await h.Service.RestoreAsync("article", articleId); // already live

        Assert.NotNull(restored); // still returns the live projection
        var after = (Article)(await h.Repository.GetByIdAsync("article", articleId, DeletedFilter.With))!;
        Assert.Equal(versionBefore, after.Version);
        Assert.DoesNotContain(await h.RevisionStore.ListAsync("article", articleId), r => r.Operation == "restore");
    }

    // ── (d) a mid-transaction failure at the revision-capture step rolls EVERYTHING back ──────────

    [Fact]
    public async Task Soft_delete_rolls_back_when_revision_capture_throws()
    {
        using var h = TrashRevisionHarness.Create(failCaptureRevision: true);
        var articleId = await h.CreateArticleAsync(status: "published"); // rev 1 "create" via real store

        var before = (Article)(await h.Repository.GetByIdAsync("article", articleId, DeletedFilter.With))!;
        var versionBefore = before.Version;

        await Assert.ThrowsAnyAsync<Exception>(
            () => h.Service.DeleteAsync("article", articleId, purge: false));

        // The soft-delete stamp AND the version bump must have rolled back with the failed capture.
        var stillLive = (Article)(await h.Repository.GetByIdAsync("article", articleId, DeletedFilter.With))!;
        Assert.Null(stillLive.DeletedAt);
        Assert.Equal(versionBefore, stillLive.Version);

        // No half-written "delete" revision — only the original "create".
        var revs = await h.RevisionStore.ListAsync("article", articleId);
        Assert.DoesNotContain(revs, r => r.Operation == "delete");
    }
}

/// <summary>
/// Wires a real <see cref="ItemService"/> over the sample Blog types plus the shared
/// <see cref="CascadeNode"/> fixture (declared in <c>PurgeIntegrityTests.cs</c>). Mirrors
/// <c>PurgeIntegrityHarness</c>. When <paramref name="failCaptureRevision"/> is set, the wired
/// <see cref="IRevisionStore"/> throws from <c>CaptureAsync</c> only (list/get still delegate to the
/// real store) so the rollback-atomicity test can prove a capture failure unwinds the soft-delete
/// stamp and version bump made in the SAME transaction.
/// </summary>
internal sealed class TrashRevisionHarness : IDisposable
{
    private readonly SqliteTestDatabase _file = new();

    public IItemRepository Repository { get; }
    public ItemService Service { get; }

    /// <summary>The real DB-backed revision store, even when <see cref="Service"/> was wired with a
    /// throwing-on-capture wrapper around it.</summary>
    public IRevisionStore RevisionStore { get; }

    private TrashRevisionHarness(bool failCaptureRevision)
    {
        var currentUser = new TestCurrentUserAccessor(Guid.Empty);
        var db = SqlSugarClientFactory.Create(
            new DatabaseOptions { DbType = StruoDbType.Sqlite, ConnectionString = _file.ConnectionString },
            currentUser);
        db.CodeFirst.InitTables<Article>();
        db.CodeFirst.InitTables<ArticleTranslation>();
        db.CodeFirst.InitTables<Language>();
        db.CodeFirst.InitTables<Tag>();
        db.CodeFirst.InitTables<ArticleTag>();
        db.CodeFirst.InitTables<Category>();
        db.CodeFirst.InitTables<Revision>();
        db.CodeFirst.InitTables<CascadeNode>();
        db.CodeFirst.InitTables<CascadeNodeTranslation>();
        LanguageSeeder.SeedAsync(db).GetAwaiter().GetResult();

        var types = new[]
        {
            typeof(Article), typeof(Category), typeof(Tag), typeof(Struo.Infrastructure.Files.File),
            typeof(CascadeNode), typeof(Struo.Infrastructure.Files.MediaFolder),
        };
        var collections = MetadataScanner.ScanTypes(types);
        var provider = new CachedMetadataProvider(collections);
        var registry = new EntityRegistry(MetadataScanner.ScanDescriptors(types));
        var collectionTypes = new Dictionary<string, Type>(StringComparer.OrdinalIgnoreCase)
        {
            ["article"]     = typeof(Article),
            ["category"]    = typeof(Category),
            ["tag"]         = typeof(Tag),
            ["file"]        = typeof(Struo.Infrastructure.Files.File),
            ["cascadeNode"] = typeof(CascadeNode),
            ["mediafolder"] = typeof(Struo.Infrastructure.Files.MediaFolder),
        };
        var graph = new RelationshipGraph(collections, collectionTypes);
        var repo = new SqlSugarItemRepository(db, registry, graph, provider, new StruoQueryOptions());
        var resolver = new RelationFilterResolver(repo, graph, provider, registry, new StruoQueryOptions());
        var expander = new RelationExpander(repo, graph, resolver, new StruoQueryOptions());
        var languages = new LanguageProvider(db);
        var realStore = new SqlSugarRevisionStore(db, currentUser);
        IRevisionStore serviceStore = failCaptureRevision ? new ThrowingOnCaptureRevisionStore(realStore) : realStore;
        var snapshotBuilder = new RevisionSnapshotBuilder(repo, provider, registry, graph);

        Repository = repo;
        RevisionStore = realStore;
        Service = new ItemService(repo, provider, registry, new AllowAllPermissionService(),
            graph, expander, graph, resolver, languages, new StruoQueryOptions(), new GanssHtmlSanitizer(),
            currentUser, serviceStore, snapshotBuilder);
    }

    public static TrashRevisionHarness Create(bool failCaptureRevision = false) => new(failCaptureRevision);

    public void Dispose() => _file.Dispose();

    private static JsonElement BodyOf(object obj)
    {
        using var doc = JsonDocument.Parse(JsonSerializer.Serialize(obj));
        return doc.RootElement.Clone();
    }

    public async Task<string> CreateArticleAsync(string status)
    {
        var body = new Dictionary<string, object?>
        {
            ["status"] = status,
            ["translations"] = new Dictionary<string, object> { ["en"] = new { title = "T" } },
        };
        var created = await Service.CreateAsync("article", BodyOf(body));
        return created["id"]!.ToString()!;
    }

    public async Task<string> CreateCategoryAsync(string name)
    {
        var created = await Service.CreateAsync("category", BodyOf(new { name }));
        return created["id"]!.ToString()!;
    }

    public async Task<string> CreateCascadeNodeAsync(string name)
    {
        var body = new Dictionary<string, object?>
        {
            ["name"] = name,
            ["translations"] = new Dictionary<string, object> { ["en"] = new { title = name } },
        };
        var created = await Service.CreateAsync("cascadeNode", BodyOf(body));
        return created["id"]!.ToString()!;
    }

    /// <summary>Capture of a "delete"/"restore" revision throws; "create" (and everything else) plus
    /// list/get delegate normally — so a row can be created first, then its trash-time capture fails.</summary>
    private sealed class ThrowingOnCaptureRevisionStore(IRevisionStore inner) : IRevisionStore
    {
        public Task CaptureAsync(string collection, string itemId, string operation, string snapshotJson, CancellationToken ct = default) =>
            operation is "delete" or "restore"
                ? throw new InvalidOperationException("Simulated capture failure (test): revision capture failed.")
                : inner.CaptureAsync(collection, itemId, operation, snapshotJson, ct);
        public Task<IReadOnlyList<RevisionInfo>> ListAsync(string collection, string itemId, CancellationToken ct = default) =>
            inner.ListAsync(collection, itemId, ct);
        public Task<RevisionRecord?> GetAsync(string collection, string itemId, long revisionNumber, CancellationToken ct = default) =>
            inner.GetAsync(collection, itemId, revisionNumber, ct);
        public Task DeleteForItemAsync(string collection, string itemId, CancellationToken ct = default) =>
            inner.DeleteForItemAsync(collection, itemId, ct);
    }
}
