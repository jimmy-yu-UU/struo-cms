// tests/Struo.Tests/Revisions/RevisionServiceTests.cs
using System.Text.Json;
using Struo.Application.Configuration;
using Struo.Application.Query;
using Struo.Application.Revisions;
using Struo.Application.Security;
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

    private RevisionServiceHarness(bool failCapture, bool canRead)
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

        IPermissionService perms = canRead ? new AllowAllPermissionService() : new DenyReadPermissionService();

        Repository = repo;
        Store = realStore;
        Service = new ItemService(repo, provider, registry, perms,
            graph, expander, graph, resolver, languages, new StruoQueryOptions(), new GanssHtmlSanitizer(),
            revisionUser, serviceStore, snapshotBuilder);
    }

    public static RevisionServiceHarness Create(bool failCapture = false, bool canRead = true) =>
        new(failCapture, canRead);

    public void Dispose() => _file.Dispose();

    private static JsonElement BodyOf(object obj)
    {
        using var doc = JsonDocument.Parse(JsonSerializer.Serialize(obj));
        return doc.RootElement.Clone();
    }

    /// <summary>A partial update body (e.g. <c>{"status":"published"}</c>).</summary>
    public JsonElement Body(string status) => BodyOf(new { status });

    /// <summary>A create body setting Article's Hidden own-field <c>internalNote</c> (SEC-2 fixture).</summary>
    public JsonElement ArticleBodyWithInternalNote(string status, string internalNote) => BodyOf(new
    {
        status,
        internalNote,
        translations = new Dictionary<string, object> { ["en"] = new { title = "T" } }
    });

    /// <summary>A create body with en/zh-TW translations that also set the Hidden+Translatable
    /// <c>internalSlug</c> field on Article's translation sidecar (SEC-2 fixture).</summary>
    public JsonElement ArticleBodyWithInternalSlug() => BodyOf(new
    {
        status = "draft",
        translations = new Dictionary<string, object>
        {
            ["en"] = new { title = "T-en", internalSlug = "secret-en" },
            ["zh-TW"] = new { title = "T-zh", internalSlug = "secret-zh" },
        }
    });

    /// <summary>A partial update body re-pointing the M2O category FK and replacing the M2M tag set
    /// (e.g. to move an article to a different category and clear its tags).</summary>
    public JsonElement Body(Guid category, Guid[] tags) => BodyOf(new { categoryId = category, tags });

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

    public async Task<string> CreateTagAsync(string name)
    {
        var created = await Service.CreateAsync("tag", BodyOf(new { name }));
        return created["id"]!.ToString()!;
    }

    /// <summary>Seeds an article referencing a category (M2O) and a tag (M2M), plus an en/zh-TW
    /// translation, then returns the article id and the two category ids used by the revert test
    /// (rev 1 = <c>catA</c>/<c>tag</c>; the caller subsequently updates to <c>catB</c>/no tags for rev 2).</summary>
    public async Task<(string id, Guid catA, Guid catB, Guid tag)> SeedArticleForRevertAsync()
    {
        var catA = Guid.Parse(await CreateCategoryAsync("A"));
        var catB = Guid.Parse(await CreateCategoryAsync("B"));
        var tag = Guid.Parse(await CreateTagAsync("T"));

        var created = await Service.CreateAsync("article", BodyOf(new
        {
            status = "draft",
            categoryId = catA,
            tags = new[] { tag },
            translations = new Dictionary<string, object>
            {
                ["en"] = new { title = "T-en" },
                ["zh-TW"] = new { title = "T-zh" },
            }
        }));
        var id = created["id"]!.ToString()!;
        return (id, catA, catB, tag);
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

    /// <summary>Denies <see cref="CanRead"/> only, so <c>ListRevisionsAsync</c>/<c>GetRevisionAsync</c>'s
    /// read gate can be exercised in isolation; writes stay allowed (unused by the read-gate test, but
    /// kept true so this harness variant could seed data itself if a future test needs it).</summary>
    private sealed class DenyReadPermissionService : IPermissionService
    {
        public bool CanRead(string collection) => false;
        public bool CanWrite(string collection) => true;
        public bool CanDelete(string collection) => true;
        public bool IsSuperAdmin => true;
        public IReadOnlyCollection<string> ReadableFields(string collection, IEnumerable<string> allFieldNames) =>
            allFieldNames.ToList();
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

    [Fact]
    public async Task Revert_reapplies_snapshot_and_appends_revert_revision()
    {
        using var h = RevisionServiceHarness.Create();
        var id = await h.CreateArticleAsync(status: "draft");                       // rev 1 (create), status=draft
        await h.Service.UpdateAsync("article", id, h.Body(status: "published"), default); // rev 2, status=published

        var reverted = await h.Service.RevertAsync("article", id, 1, default);      // revert to rev 1 (draft)
        Assert.NotNull(reverted);
        Assert.Equal("draft", reverted!["status"]);                                 // current state matches rev 1

        var list = await h.Service.ListRevisionsAsync("article", id, default);
        Assert.Equal(3, list.Count);                                                // append-only: 1,2 preserved + revert
        Assert.Equal("revert", list[0].Operation);
    }

    [Fact]
    public async Task Revert_restores_relations_and_translations()
    {
        using var h = RevisionServiceHarness.Create();
        var (id, catA, catB, tag) = await h.SeedArticleForRevertAsync();            // rev1: category=catA, tags=[tag], zh-TW title
        await h.Service.UpdateAsync("article", id, h.Body(category: catB, tags: []), default); // rev2: catB, no tags

        await h.Service.RevertAsync("article", id, 1, default);
        var now = await h.Service.GetAsync("article", id, DeepFor("category", "tags"), null, default);
        Assert.Equal(catA.ToString(), CategoryIdOf(now!));                          // M2O FK restored
        Assert.Single(TagsOf(now!));                                                // M2M restored
        Assert.Equal(tag.ToString(), TagIdOf(now!, 0));
        Assert.Equal("T-zh", ZhTwTitleOf(now!));                                    // zh-TW translation restored
    }

    [Fact]
    public async Task Revert_unknown_revision_returns_null()
    {
        using var h = RevisionServiceHarness.Create();
        var id = await h.CreateArticleAsync(status: "draft");
        Assert.Null(await h.Service.RevertAsync("article", id, 99, default));       // -> 404
    }

    [Fact]
    public async Task GetRevision_returns_snapshot_and_list_is_read_gated()
    {
        using var h = RevisionServiceHarness.Create();
        var id = await h.CreateArticleAsync(status: "draft");
        var rec = await h.Service.GetRevisionAsync("article", id, 1, default);
        Assert.NotNull(rec);
        Assert.Contains("\"status\"", rec!.Snapshot, StringComparison.Ordinal);

        using var noRead = RevisionServiceHarness.Create(canRead: false);
        await Assert.ThrowsAsync<PermissionDeniedException>(
            () => noRead.Service.ListRevisionsAsync("article", id, default));
    }

    /// <summary>SEC-2: <c>GetRevisionAsync</c> is the externally-facing read (backs REST/GraphQL) — its
    /// snapshot must have <c>internalNote</c> (a <c>[CmsField(Hidden = true)]</c> own-field on the sample
    /// Article) redacted, even though <see cref="RevisionSnapshotBuilder"/> captured it in full.</summary>
    [Fact]
    public async Task GetRevision_redacts_hidden_own_field()
    {
        using var h = RevisionServiceHarness.Create();
        var created = await h.Service.CreateAsync("article", h.ArticleBodyWithInternalNote("draft", "secret-token"));
        var id = created["id"]!.ToString()!;

        var rec = await h.Service.GetRevisionAsync("article", id, 1, default);
        Assert.NotNull(rec);
        Assert.DoesNotContain("secret-token", rec!.Snapshot, StringComparison.Ordinal);
        Assert.DoesNotContain("internalNote", rec.Snapshot, StringComparison.Ordinal);
        Assert.Contains("\"status\":\"draft\"", rec.Snapshot, StringComparison.Ordinal); // non-hidden untouched
    }

    /// <summary>SEC-2: the same hidden field must also be stripped out of every
    /// <c>translations.{locale}</c> object (Article's <c>internalSlug</c> is Hidden+Translatable).</summary>
    [Fact]
    public async Task GetRevision_redacts_hidden_translatable_field_in_every_locale()
    {
        using var h = RevisionServiceHarness.Create();
        var created = await h.Service.CreateAsync("article", h.ArticleBodyWithInternalSlug());
        var id = created["id"]!.ToString()!;

        var rec = await h.Service.GetRevisionAsync("article", id, 1, default);
        Assert.NotNull(rec);
        using var doc = JsonDocument.Parse(rec!.Snapshot);
        var translations = doc.RootElement.GetProperty("translations");
        Assert.False(translations.GetProperty("en").TryGetProperty("internalSlug", out _));
        Assert.False(translations.GetProperty("zh-TW").TryGetProperty("internalSlug", out _));
        Assert.Equal("T-en", translations.GetProperty("en").GetProperty("title").GetString());
        Assert.Equal("T-zh", translations.GetProperty("zh-TW").GetProperty("title").GetString());
    }

    /// <summary>SEC-2 counter-proof: <c>RevertAsync</c> must NOT go through the redacted read — it reads
    /// the revision store directly (<c>ItemService.cs:710</c>), so a hidden field's value is still
    /// restored on revert even though the externally-returned snapshot omits it.</summary>
    [Fact]
    public async Task Revert_restores_hidden_field_value_despite_external_redaction()
    {
        using var h = RevisionServiceHarness.Create();
        var created = await h.Service.CreateAsync("article", h.ArticleBodyWithInternalNote("draft", "secret-token")); // rev 1
        var id = created["id"]!.ToString()!;
        await h.Service.UpdateAsync("article", id, h.Body(status: "published"), default); // rev 2, clears nothing but changes status

        // Confirm the externally-visible view (GetRevisionAsync) is redacted, as above.
        var rec = await h.Service.GetRevisionAsync("article", id, 1, default);
        Assert.DoesNotContain("secret-token", rec!.Snapshot, StringComparison.Ordinal);

        // Now revert to rev 1 and prove the hidden field's actual value came back on the live row.
        // Read the RAW entity via the repository (bypassing ItemService.Project, which itself skips
        // Hidden fields on every read) — this can only be non-null if RevertAsync used the unredacted
        // snapshot from the revision store rather than flowing through GetRevisionAsync's redaction.
        var reverted = await h.Service.RevertAsync("article", id, 1, default);
        Assert.NotNull(reverted);
        var rawEntity = (Struo.Sample.Blog.Article)(await h.Repository.GetByIdAsync("article", id, DeletedFilter.Exclude, default))!;
        Assert.Equal("secret-token", rawEntity.InternalNote);
    }

    /// <summary>Builds a flat <see cref="DeepSpec"/> requesting the given top-level relation names
    /// (no field whitelist, no nested args) — enough for the revert-restore test to read back the
    /// M2O <c>category</c> object and the M2M <c>tags</c> array on the projected item.</summary>
    private static DeepSpec DeepFor(params string[] relations) =>
        new(relations.ToDictionary(r => r, _ => new DeepRelationSpec(null, null), StringComparer.OrdinalIgnoreCase));

    private static string CategoryIdOf(IReadOnlyDictionary<string, object?> row) =>
        ((IReadOnlyDictionary<string, object?>)row["category"]!)["id"]!.ToString()!;

    private static IReadOnlyList<IReadOnlyDictionary<string, object?>> TagsOf(IReadOnlyDictionary<string, object?> row) =>
        (IReadOnlyList<IReadOnlyDictionary<string, object?>>)row["tags"]!;

    private static string TagIdOf(IReadOnlyDictionary<string, object?> row, int index) =>
        TagsOf(row)[index]["id"]!.ToString()!;

    /// <summary>Reads the <c>zh-TW</c> title out of the <c>translations</c> overlay (populated
    /// because <see cref="ItemService.GetAsync"/> is called with a <c>null</c> locale, which loads
    /// every locale rather than filtering to one) — used to prove a seeded translation round-trips
    /// through <c>RevertAsync</c>.</summary>
    private static string ZhTwTitleOf(IReadOnlyDictionary<string, object?> row) =>
        (string)((IReadOnlyDictionary<string, Dictionary<string, object?>>)row["translations"]!)["zh-TW"]["title"]!;
}
