// tests/Struo.Tests/Query/PurgeIntegrityTests.cs
using System.Text.Json;
using SqlSugar;
using Struo.Application.Configuration;
using Struo.Application.Localization;
using Struo.Application.Query;
using Struo.Application.Revisions;
using Struo.Domain.Auditing;
using Struo.Domain.Metadata.Attributes;
using Struo.Domain.Metadata.Enums;
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

// ── Test-local entities ────────────────────────────────
//
// The sample Blog domain has no OnDelete.Restrict or OnDelete.Cascade relation (Article.Category
// and Category.Parent are both SetNull — see SoftDeleteRepositoryTests's note on this), so the
// Restrict and Cascade purge scenarios need dedicated fixtures, mirroring the brief's guidance to
// add "test-local entity pairs" the same way MetadataScannerTests declares local [CmsCollection]
// types for a single test file.

/// <summary>
/// Self-referencing, soft-deletable, revisioned node with a translation sidecar — one type covers
/// three scenarios: plain Cascade (parent -> child), cyclic Cascade (two nodes pointing at each
/// other) must not infinite-loop, and a Cascade source row that is already soft-deleted (trashed)
/// must still be found and cascade-purged (not silently skipped by the soft-delete query filter).
/// </summary>
[SugarTable("test_cascade_nodes")]
[CmsCollection("CascadeNode", Revisions = true)]
internal sealed class CascadeNode : ISoftDeletable
{
    [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }

    public DateTime? DeletedAt { get; set; }
    public Guid? DeletedBy { get; set; }

    [CmsTranslations(typeof(CascadeNodeTranslation))]
    [SugarColumn(IsIgnore = true)]
    public List<CascadeNodeTranslation> Translations { get; set; } = [];

    [CmsField(Label = "Name", Interface = FieldInterface.Text)]
    public string Name { get; set; } = "";

    [SugarColumn(IsNullable = true)]
    public Guid? ParentId { get; set; }

    [Navigate(NavigateType.OneToOne, nameof(ParentId))]
    [CmsRelation(Interface = RelationInterface.Dropdown, OnDelete = OnDelete.Cascade)]
    [SugarColumn(IsIgnore = true)]
    public CascadeNode? Parent { get; set; }
}

[SugarTable("test_cascade_node_translations")]
internal sealed class CascadeNodeTranslation
{
    [SugarColumn(IsPrimaryKey = true, IsIdentity = true)] public long Id { get; set; }
    public Guid CascadeNodeId { get; set; }
    public string Locale { get; set; } = "";

    [CmsField(Label = "Title", Interface = FieldInterface.Text, Required = true)]
    public string Title { get; set; } = "";
}

/// <summary>
/// A plain M2O-to-Category relation declared OnDelete.Restrict — the sample domain has none (both
/// of its M2O relations are SetNull), so this fixture is the only way to exercise the Restrict
/// purge-guard against a real SQLite-backed pipeline.
/// </summary>
[SugarTable("test_restrict_refs")]
[CmsCollection("RestrictRef")]
internal sealed class RestrictRef
{
    [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }

    [CmsField(Label = "Name", Interface = FieldInterface.Text, Required = true)]
    public string Name { get; set; } = "";

    public Guid CategoryId { get; set; }

    [Navigate(NavigateType.OneToOne, nameof(CategoryId))]
    [CmsRelation(Interface = RelationInterface.Dropdown, OnDelete = OnDelete.Restrict)]
    [SugarColumn(IsIgnore = true)]
    public Category? Category { get; set; }
}

/// <summary>
/// Wires a real <see cref="ItemService"/> over the sample Blog types PLUS the local
/// <see cref="CascadeNode"/>/<see cref="RestrictRef"/> fixtures above, mirroring
/// <c>RevisionServiceHarness</c>'s construction. When <paramref name="failDeleteRevision"/> is set,
/// the wired <see cref="IRevisionStore"/> throws from <c>DeleteForItemAsync</c> only (capture/list/get
/// still delegate to the real store) so the rollback-atomicity test can prove a failure late in the
/// purge pipeline unwinds everything already deleted earlier in the SAME transaction.
/// </summary>
internal sealed class PurgeIntegrityHarness : IDisposable
{
    private readonly SqliteTestDatabase _file = new();

    public IItemRepository Repository { get; }
    public ItemService Service { get; }

    /// <summary>The real revision store — always DB-backed, even when <see cref="Service"/> itself
    /// was wired with a throwing wrapper around it (rollback-atomicity test).</summary>
    public IRevisionStore RevisionStore { get; }

    private PurgeIntegrityHarness(bool failDeleteRevision)
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
        db.CodeFirst.InitTables<CascadeNode>();
        db.CodeFirst.InitTables<CascadeNodeTranslation>();
        db.CodeFirst.InitTables<RestrictRef>();
        LanguageSeeder.SeedAsync(db).GetAwaiter().GetResult();

        var types = new[]
        {
            typeof(Article), typeof(Category), typeof(Tag), typeof(Struo.Infrastructure.Files.File),
            typeof(CascadeNode), typeof(RestrictRef), typeof(Struo.Infrastructure.Files.MediaFolder),
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
            ["restrictRef"] = typeof(RestrictRef),
            ["mediafolder"] = typeof(Struo.Infrastructure.Files.MediaFolder),
        };
        var graph = new RelationshipGraph(collections, collectionTypes);
        var repo = new SqlSugarItemRepository(db, registry, graph, provider, new StruoQueryOptions());
        var resolver = new RelationFilterResolver(repo, graph, provider, registry, new StruoQueryOptions());
        var expander = new RelationExpander(repo, graph, resolver, new StruoQueryOptions());
        var languages = new LanguageProvider(db);
        var currentUser = new TestCurrentUserAccessor(Guid.Empty);
        var realStore = new SqlSugarRevisionStore(db, currentUser);
        IRevisionStore serviceStore = failDeleteRevision ? new ThrowingOnDeleteRevisionStore(realStore) : realStore;
        var snapshotBuilder = new RevisionSnapshotBuilder(repo, provider, registry, graph);

        Repository = repo;
        RevisionStore = realStore;
        Service = new ItemService(repo, provider, registry, new AllowAllPermissionService(),
            graph, expander, graph, resolver, languages, new StruoQueryOptions(), new GanssHtmlSanitizer(),
            currentUser, serviceStore, snapshotBuilder);
    }

    public static PurgeIntegrityHarness Create(bool failDeleteRevision = false) => new(failDeleteRevision);

    public void Dispose() => _file.Dispose();

    // ── body / create helpers ───────────────────────────────────────────────

    private static JsonElement BodyOf(object obj)
    {
        using var doc = JsonDocument.Parse(JsonSerializer.Serialize(obj));
        return doc.RootElement.Clone();
    }

    public async Task<string> CreateTagAsync(string name)
    {
        var created = await Service.CreateAsync("tag", BodyOf(new { name }));
        return created["id"]!.ToString()!;
    }

    public async Task<string> CreateCategoryAsync(string name)
    {
        var created = await Service.CreateAsync("category", BodyOf(new { name }));
        return created["id"]!.ToString()!;
    }

    public async Task<string> CreateArticleAsync(Guid? categoryId = null, Guid[]? tags = null)
    {
        var body = new Dictionary<string, object?>
        {
            ["status"] = "draft",
            ["translations"] = new Dictionary<string, object> { ["en"] = new { title = "T" } },
        };
        if (categoryId is not null) body["categoryId"] = categoryId.Value;
        if (tags is not null) body["tags"] = tags;
        var created = await Service.CreateAsync("article", BodyOf(body));
        return created["id"]!.ToString()!;
    }

    public async Task<string> CreateRestrictRefAsync(string name, Guid categoryId)
    {
        var created = await Service.CreateAsync("restrictRef", BodyOf(new { name, categoryId }));
        return created["id"]!.ToString()!;
    }

    public async Task<string> CreateCascadeNodeAsync(string name, Guid? parentId = null)
    {
        var body = new Dictionary<string, object?>
        {
            ["name"] = name,
            ["translations"] = new Dictionary<string, object> { ["en"] = new { title = name } },
        };
        if (parentId is not null) body["parentId"] = parentId.Value;
        var created = await Service.CreateAsync("cascadeNode", BodyOf(body));
        return created["id"]!.ToString()!;
    }

    /// <summary>
    /// Repoints ParentId directly via the repository, bypassing <see cref="ItemService"/> (and its
    /// <see cref="SelfReferenceCycleGuard"/>). This simulates a cycle that already exists in the data
    /// — e.g. from before the guard shipped, or written by another process — which is exactly the
    /// scenario <c>Purge_cyclic_cascade_terminates_and_deletes_both_nodes</c> exercises: the purge
    /// pipeline's own termination defense against corrupt/cyclic data, independent of how it arose.
    /// Going through <see cref="Service"/> here would now be rejected by the guard before a cycle
    /// could ever be constructed.
    /// </summary>
    public async Task RepointCascadeNodeParentAsync(string id, Guid? parentId)
    {
        var node = (CascadeNode)(await Repository.GetByIdAsync("cascadeNode", id))!;
        node.ParentId = parentId;
        await Repository.UpdateAsync("cascadeNode", id, node);
    }

    // ── raw junction/translation existence checks (via the repository, not the DB directly) ────

    public Task<IReadOnlyList<object>> ArticleTagsByArticleAsync(Guid articleId) =>
        Repository.QueryEntityWhereInAsync(typeof(ArticleTag), "ArticleId", [articleId]);

    public Task<IReadOnlyList<object>> ArticleTagsByTagAsync(Guid tagId) =>
        Repository.QueryEntityWhereInAsync(typeof(ArticleTag), "TagId", [tagId]);

    public Task<IReadOnlyList<object>> ArticleTranslationsAsync(Guid articleId) =>
        Repository.QueryEntityWhereInAsync(typeof(ArticleTranslation), "ArticleId", [articleId]);

    public Task<IReadOnlyList<object>> CascadeNodeTranslationsAsync(Guid nodeId) =>
        Repository.QueryEntityWhereInAsync(typeof(CascadeNodeTranslation), "CascadeNodeId", [nodeId]);

    /// <summary>Simulates a capture-time-safe / delete-time-failing revision store: capture/list/get
    /// delegate normally, but <c>DeleteForItemAsync</c> always throws — used to prove a failure late
    /// in the purge pipeline rolls back everything the SAME transaction already deleted earlier
    /// (junction rows, translation rows).</summary>
    private sealed class ThrowingOnDeleteRevisionStore(IRevisionStore inner) : IRevisionStore
    {
        public Task CaptureAsync(string collection, string itemId, string operation, string snapshotJson,
            long? sourceRevisionNumber = null, CancellationToken ct = default) =>
            inner.CaptureAsync(collection, itemId, operation, snapshotJson, sourceRevisionNumber, ct);
        public Task<IReadOnlyList<RevisionInfo>> ListAsync(string collection, string itemId, CancellationToken ct = default) =>
            inner.ListAsync(collection, itemId, ct);
        public Task<RevisionRecord?> GetAsync(string collection, string itemId, long revisionNumber, CancellationToken ct = default) =>
            inner.GetAsync(collection, itemId, revisionNumber, ct);
        public Task DeleteForItemAsync(string collection, string itemId, CancellationToken ct = default) =>
            throw new InvalidOperationException("Simulated purge failure (test): revision delete failed.");
    }
}

/// <summary>
/// Purging an item must not orphan its junction/translation/revision rows, and
/// must honour OnDelete.SetNull/Cascade (previously silently no-op — only Restrict was implemented),
/// all inside one rollback-safe transaction. Real SQLite-backed <see cref="ItemService"/>, no stubs.
/// </summary>
public sealed class PurgeIntegrityTests
{
    // ── Scenario 1: purge Article clears its translations, M2M tag junctions, and revisions ──────

    [Fact]
    public async Task Purge_article_clears_translations_tags_and_revisions()
    {
        using var h = PurgeIntegrityHarness.Create();
        var tagId = Guid.Parse(await h.CreateTagAsync("T1"));
        var articleId = Guid.Parse(await h.CreateArticleAsync(tags: [tagId]));
        await h.Service.UpdateAsync("article", articleId.ToString(),
            JsonSerializer.SerializeToElement(new { status = "published" })); // 2nd revision

        // Sanity: all three row kinds exist before purge.
        Assert.NotEmpty(await h.ArticleTranslationsAsync(articleId));
        Assert.NotEmpty(await h.ArticleTagsByArticleAsync(articleId));
        Assert.Equal(2, (await h.RevisionStore.ListAsync("article", articleId.ToString())).Count);

        var purged = await h.Service.DeleteAsync("article", articleId.ToString(), purge: true);

        Assert.True(purged);
        Assert.Empty(await h.ArticleTranslationsAsync(articleId));
        Assert.Empty(await h.ArticleTagsByArticleAsync(articleId));
        Assert.Empty(await h.RevisionStore.ListAsync("article", articleId.ToString()));
        Assert.Null(await h.Repository.GetByIdAsync("article", articleId.ToString(), DeletedFilter.With));
        // The tag itself (the M2M's OTHER side) must survive an article purge.
        Assert.NotNull(await h.Repository.GetByIdAsync("tag", tagId.ToString()));
    }

    // ── Scenario 2: purge Tag clears the INBOUND (target-side) article_tags junction rows ────────

    [Fact]
    public async Task Purge_tag_clears_inbound_article_tag_junctions()
    {
        using var h = PurgeIntegrityHarness.Create();
        var tagId = Guid.Parse(await h.CreateTagAsync("T1"));
        var articleId = Guid.Parse(await h.CreateArticleAsync(tags: [tagId]));
        Assert.NotEmpty(await h.ArticleTagsByTagAsync(tagId));

        var purged = await h.Service.DeleteAsync("tag", tagId.ToString(), purge: true);

        Assert.True(purged);
        Assert.Empty(await h.ArticleTagsByTagAsync(tagId));
        Assert.Null(await h.Repository.GetByIdAsync("tag", tagId.ToString(), DeletedFilter.With));
        // The article itself (owning side of the relation) must survive a tag purge.
        Assert.NotNull(await h.Repository.GetByIdAsync("article", articleId.ToString()));
    }

    // ── Scenario 3: purge a referenced Category (Article.Category is SetNull) ──────────────────

    [Fact]
    public async Task Purge_category_nulls_referencing_article_fk_and_article_survives()
    {
        using var h = PurgeIntegrityHarness.Create();
        var categoryId = Guid.Parse(await h.CreateCategoryAsync("Cat"));
        var articleId = Guid.Parse(await h.CreateArticleAsync(categoryId: categoryId));

        var purged = await h.Service.DeleteAsync("category", categoryId.ToString(), purge: true);

        Assert.True(purged);
        var article = (Article)(await h.Repository.GetByIdAsync("article", articleId.ToString()))!;
        Assert.Null(article.CategoryId);
        Assert.Null(await h.Repository.GetByIdAsync("category", categoryId.ToString(), DeletedFilter.With));
    }

    // ── Scenario 4a: Cascade — purge parent deletes child, incl. child's translations/revisions ──

    [Fact]
    public async Task Purge_cascade_deletes_child_and_its_own_translations_and_revisions()
    {
        using var h = PurgeIntegrityHarness.Create();
        var parentId = Guid.Parse(await h.CreateCascadeNodeAsync("Parent"));
        var childId = Guid.Parse(await h.CreateCascadeNodeAsync("Child", parentId));
        await h.Service.UpdateAsync("cascadeNode", childId.ToString(),
            JsonSerializer.SerializeToElement(new { name = "Child-v2" })); // 2nd revision on the child

        Assert.NotEmpty(await h.CascadeNodeTranslationsAsync(childId));
        Assert.Equal(2, (await h.RevisionStore.ListAsync("cascadeNode", childId.ToString())).Count);

        var purged = await h.Service.DeleteAsync("cascadeNode", parentId.ToString(), purge: true);

        Assert.True(purged);
        Assert.Null(await h.Repository.GetByIdAsync("cascadeNode", parentId.ToString(), DeletedFilter.With));
        Assert.Null(await h.Repository.GetByIdAsync("cascadeNode", childId.ToString(), DeletedFilter.With));
        Assert.Empty(await h.CascadeNodeTranslationsAsync(childId));
        Assert.Empty(await h.RevisionStore.ListAsync("cascadeNode", childId.ToString()));
    }

    // ── Scenario 4b: cyclic Cascade must terminate, not infinite-loop ─────────────────────────────

    [Fact]
    public async Task Purge_cyclic_cascade_terminates_and_deletes_both_nodes()
    {
        using var h = PurgeIntegrityHarness.Create();
        var node1Id = Guid.Parse(await h.CreateCascadeNodeAsync("N1"));
        var node2Id = Guid.Parse(await h.CreateCascadeNodeAsync("N2", node1Id)); // N2.Parent = N1
        await h.RepointCascadeNodeParentAsync(node1Id.ToString(), node2Id);      // N1.Parent = N2 (cycle)

        var task = h.Service.DeleteAsync("cascadeNode", node1Id.ToString(), purge: true);
        var finished = await Task.WhenAny(task, Task.Delay(TimeSpan.FromSeconds(10))) == task;

        Assert.True(finished, "Purge of a cyclic Cascade graph did not terminate (possible infinite loop).");
        Assert.True(await task);
        Assert.Null(await h.Repository.GetByIdAsync("cascadeNode", node1Id.ToString(), DeletedFilter.With));
        Assert.Null(await h.Repository.GetByIdAsync("cascadeNode", node2Id.ToString(), DeletedFilter.With));
    }

    // ── Scenario 5: Restrict still blocks purge (and blocks it before anything is touched) ───────

    [Fact]
    public async Task Purge_blocked_by_restrict_throws_and_leaves_everything_intact()
    {
        using var h = PurgeIntegrityHarness.Create();
        var categoryId = Guid.Parse(await h.CreateCategoryAsync("Cat"));
        var refId = Guid.Parse(await h.CreateRestrictRefAsync("Ref", categoryId));

        await Assert.ThrowsAsync<RelationConflictException>(
            () => h.Service.DeleteAsync("category", categoryId.ToString(), purge: true));

        Assert.NotNull(await h.Repository.GetByIdAsync("category", categoryId.ToString()));
        Assert.NotNull(await h.Repository.GetByIdAsync("restrictRef", refId.ToString()));
    }

    // ── Scenario 6: a mid-pipeline failure rolls back EVERYTHING, including steps already run ────

    [Fact]
    public async Task Purge_failure_mid_pipeline_rolls_back_junction_and_translation_deletes_too()
    {
        using var h = PurgeIntegrityHarness.Create(failDeleteRevision: true);
        var tagId = Guid.Parse(await h.CreateTagAsync("T1"));
        var articleId = Guid.Parse(await h.CreateArticleAsync(tags: [tagId])); // rev 1 captured

        // Pipeline order is: SetNull -> Cascade -> M2M junctions -> translations -> revisions -> parent
        // row. The injected failure sits at the revisions step, AFTER the junction and translation
        // deletes already executed inside the SAME transaction — proving those get rolled back too,
        // not just the (never-reached) parent-row delete.
        await Assert.ThrowsAnyAsync<Exception>(
            () => h.Service.DeleteAsync("article", articleId.ToString(), purge: true));

        Assert.NotNull(await h.Repository.GetByIdAsync("article", articleId.ToString()));
        Assert.NotEmpty(await h.ArticleTagsByArticleAsync(articleId));
        Assert.NotEmpty(await h.ArticleTranslationsAsync(articleId));
        Assert.NotEmpty(await h.RevisionStore.ListAsync("article", articleId.ToString()));
    }

    // ── Soft-deleted-source handling (context callout, not one of the 6 numbered scenarios) ──────
    // A row that is already trashed (soft-deleted) but still references the purge target must be
    // found and processed too — QueryWhereInAsync (used by the Restrict/Cascade discovery queries)
    // is filtered by the global ISoftDeletable query floor, so Cascade discovery specifically uses
    // QueryWhereInWithDeletedAsync (bypasses the floor); SetNull uses Updateable<T>, which SqlSugar
    // never subjects to the floor in the first place (see SoftDeleteGenericAsync's own comment).

    [Fact]
    public async Task Purge_setnull_reaches_an_already_trashed_referencing_article()
    {
        using var h = PurgeIntegrityHarness.Create();
        var categoryId = Guid.Parse(await h.CreateCategoryAsync("Cat"));
        var articleId = Guid.Parse(await h.CreateArticleAsync(categoryId: categoryId));
        await h.Service.DeleteAsync("article", articleId.ToString(), purge: false); // trash (not purge)
        Assert.Null(await h.Repository.GetByIdAsync("article", articleId.ToString(), DeletedFilter.Exclude));

        var purged = await h.Service.DeleteAsync("category", categoryId.ToString(), purge: true);

        Assert.True(purged);
        var trashedArticle = (Article)(await h.Repository.GetByIdAsync(
            "article", articleId.ToString(), DeletedFilter.With))!;
        Assert.Null(trashedArticle.CategoryId);
    }

    [Fact]
    public async Task Purge_cascade_reaches_an_already_trashed_child()
    {
        using var h = PurgeIntegrityHarness.Create();
        var parentId = Guid.Parse(await h.CreateCascadeNodeAsync("Parent"));
        var childId = Guid.Parse(await h.CreateCascadeNodeAsync("Child", parentId));
        await h.Service.DeleteAsync("cascadeNode", childId.ToString(), purge: false); // trash the child
        Assert.NotNull(await h.Repository.GetByIdAsync("cascadeNode", childId.ToString(), DeletedFilter.With));
        Assert.Null(await h.Repository.GetByIdAsync("cascadeNode", childId.ToString(), DeletedFilter.Exclude));

        var purged = await h.Service.DeleteAsync("cascadeNode", parentId.ToString(), purge: true);

        Assert.True(purged);
        // The trashed child must ALSO have been cascade-purged — not silently skipped because it
        // was hidden behind the soft-delete floor.
        Assert.Null(await h.Repository.GetByIdAsync("cascadeNode", childId.ToString(), DeletedFilter.With));
    }
}
