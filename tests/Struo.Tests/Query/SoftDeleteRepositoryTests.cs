// tests/Struo.Tests/Query/SoftDeleteRepositoryTests.cs
using Struo.Tests.Support.Seeding;
using AwesomeAssertions;
using SqlSugar;
using Struo.Application.Configuration;
using Struo.Application.Query;
using Struo.Domain.Auditing;
using Struo.Domain.Query;
using Struo.Infrastructure.Localization;
using Struo.Infrastructure.Metadata;
using Struo.Infrastructure.Persistence;
using Struo.Infrastructure.Query;
using Struo.Infrastructure.Revisions;
using Struo.Infrastructure.Security;
using Struo.Tests.Fixtures;
using Struo.Tests.Support;
using Xunit;

namespace Struo.Tests.Query;

// The global query filter registered in SqlSugarClientFactory must exclude
// soft-deleted rows (non-null DeletedAt) from every default Queryable over an ISoftDeletable
// entity, with no per-path code in the repository.
public class SoftDeleteRepositoryTests
{
    [Fact]
    public async Task Default_query_excludes_soft_deleted_rows()
    {
        using var h = SoftDeleteRepositoryHarness.Create();
        var live = await h.InsertArticleAsync(status: "published");
        var trashed = await h.InsertArticleAsync(status: "published");
        await h.SoftDeleteRawAsync(trashed);

        var result = await h.Repository.QueryAsync("fxArticle",
            new QueryModel(null, null, [], 100, 0, null), [], null, default);

        var ids = result.Rows.Select(SoftDeleteRepositoryHarness.IdOf).ToList();
        ids.Should().Contain(live);
        ids.Should().NotContain(trashed);
    }

    [Fact]
    public async Task Query_with_Only_returns_just_trashed()
    {
        using var h = SoftDeleteRepositoryHarness.Create();
        var live = await h.InsertArticleAsync(status: "published");
        var trashed = await h.InsertArticleAsync(status: "published");
        await h.Repository.SoftDeleteAsync("fxArticle", trashed.ToString(), DateTime.UtcNow, null, default);

        var only = await h.Repository.QueryAsync("fxArticle",
            new QueryModel(null, null, [], 100, 0, null), [], null, DeletedFilter.Only, default);
        var ids = only.Rows.Select(SoftDeleteRepositoryHarness.IdOf).ToList();
        Assert.Contains(trashed, ids);
        Assert.DoesNotContain(live, ids);
    }

    [Fact]
    public async Task Restore_makes_row_visible_again()
    {
        using var h = SoftDeleteRepositoryHarness.Create();
        var id = await h.InsertArticleAsync(status: "published");
        await h.Repository.SoftDeleteAsync("fxArticle", id.ToString(), DateTime.UtcNow, null, default);
        Assert.Null(await h.Repository.GetByIdAsync("fxArticle", id.ToString(), DeletedFilter.Exclude, default));

        var restored = await h.Repository.RestoreAsync("fxArticle", id.ToString(), default);
        Assert.True(restored);
        Assert.NotNull(await h.Repository.GetByIdAsync("fxArticle", id.ToString(), DeletedFilter.Exclude, default));
    }

    [Fact]
    public async Task GetById_with_With_finds_trashed_row()
    {
        using var h = SoftDeleteRepositoryHarness.Create();
        var id = await h.InsertArticleAsync(status: "published");
        await h.Repository.SoftDeleteAsync("fxArticle", id.ToString(), DateTime.UtcNow, null, default);
        Assert.NotNull(await h.Repository.GetByIdAsync("fxArticle", id.ToString(), DeletedFilter.With, default));
    }

    // ── ItemService soft-delete/purge/restore end-to-end ───────────────

    private static readonly Guid KnownUser = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");

    [Fact]
    public async Task Delete_soft_deletes_and_stamps_actor()
    {
        using var h = SoftDeleteRepositoryHarness.Create(currentUserId: KnownUser);
        var id = await h.InsertArticleAsync(status: "published");

        Assert.True(await h.Service.DeleteAsync("fxArticle", id.ToString(), purge: false, default));

        Assert.Null(await h.Service.GetAsync("fxArticle", id.ToString(), null, null, DeletedFilter.Exclude, default));
        var trashed = await h.Repository.GetByIdAsync("fxArticle", id.ToString(), DeletedFilter.With, default);
        Assert.NotNull(trashed);
        Assert.Equal(KnownUser, ((ISoftDeletable)trashed!).DeletedBy);
    }

    [Fact]
    public async Task Purge_hard_deletes_a_soft_delete_collection()
    {
        using var h = SoftDeleteRepositoryHarness.Create();
        var id = await h.InsertArticleAsync(status: "published");
        await h.Service.DeleteAsync("fxArticle", id.ToString(), purge: false, default);   // soft
        Assert.True(await h.Service.DeleteAsync("fxArticle", id.ToString(), purge: true, default)); // purge
        Assert.Null(await h.Repository.GetByIdAsync("fxArticle", id.ToString(), DeletedFilter.With, default));
    }

    [Fact]
    public async Task Restore_clears_marker_via_service()
    {
        using var h = SoftDeleteRepositoryHarness.Create();
        var id = await h.InsertArticleAsync(status: "published");
        await h.Service.DeleteAsync("fxArticle", id.ToString(), purge: false, default);
        var restored = await h.Service.RestoreAsync("fxArticle", id.ToString(), default);
        Assert.NotNull(restored);
        Assert.NotNull(await h.Service.GetAsync("fxArticle", id.ToString(), null, null, DeletedFilter.Exclude, default));
    }

    [Fact]
    public async Task Restore_unknown_id_returns_null()
    {
        using var h = SoftDeleteRepositoryHarness.Create();
        Assert.Null(await h.Service.RestoreAsync("fxArticle", Guid.NewGuid().ToString(), default));
    }

    // ── Restore idempotency (no double Version bump / duplicate revision) ───────────────

    /// <summary>Restoring a row that is ALREADY live (a second RestoreAsync call after the
    /// first already succeeded) must be a true no-op — no Version bump, no extra "restore" revision.
    /// The decision now comes from repository.RestoreAsync's own atomic "WHERE deletedat IS NOT NULL"
    /// UPDATE (affected-rows), not from a separate pre-read snapshot in ItemService, so this also
    /// covers the case a naive pre-read-based guard would miss under concurrent restores.</summary>
    [Fact]
    public async Task Restore_of_an_already_live_row_is_a_true_no_op()
    {
        using var h = SoftDeleteRepositoryHarness.Create();
        // InsertArticleAsync writes via the raw repository (bypassing ItemService.CreateAsync), so it
        // captures no revision — the FIRST revision on this item is the "delete" below.
        var id = await h.InsertArticleAsync(status: "published");
        await h.Service.DeleteAsync("fxArticle", id.ToString(), purge: false, default);  // rev 1 (delete)
        var first = await h.Service.RestoreAsync("fxArticle", id.ToString(), default);   // rev 2 (restore)
        Assert.NotNull(first);

        var afterFirst = (AuditableEntity)(await h.Repository.GetByIdAsync(
            "fxArticle", id.ToString(), DeletedFilter.Exclude, default))!;
        var revisionsAfterFirst = (await h.Service.ListRevisionsAsync("fxArticle", id.ToString(), default))!;
        Assert.Equal(2, revisionsAfterFirst.Count);

        // Second restore of the now-live row: still succeeds (idempotent 200, not an error) but must
        // NOT bump Version again or append a 4th revision.
        var second = await h.Service.RestoreAsync("fxArticle", id.ToString(), default);
        Assert.NotNull(second);

        var afterSecond = (AuditableEntity)(await h.Repository.GetByIdAsync(
            "fxArticle", id.ToString(), DeletedFilter.Exclude, default))!;
        var revisionsAfterSecond = (await h.Service.ListRevisionsAsync("fxArticle", id.ToString(), default))!;

        Assert.Equal(afterFirst.Version, afterSecond.Version);
        Assert.Equal(revisionsAfterFirst.Count, revisionsAfterSecond.Count);
    }

    /// <summary>Repository layer: RestoreAsync against a row that was NEVER soft-deleted (no
    /// ItemService pre-read involved at all) must report false (no row matched the atomic
    /// "deletedat IS NOT NULL" guard) and must not touch Version.</summary>
    [Fact]
    public async Task RestoreAsync_on_a_never_trashed_row_returns_false_and_does_not_bump_version()
    {
        using var h = SoftDeleteRepositoryHarness.Create();
        var id = await h.InsertArticleAsync(status: "published");
        var before = (AuditableEntity)(await h.Repository.GetByIdAsync(
            "fxArticle", id.ToString(), DeletedFilter.Exclude, default))!;

        var restored = await h.Repository.RestoreAsync("fxArticle", id.ToString(), default);
        Assert.False(restored);

        var after = (AuditableEntity)(await h.Repository.GetByIdAsync(
            "fxArticle", id.ToString(), DeletedFilter.Exclude, default))!;
        Assert.Equal(before.Version, after.Version);
    }

    // ── Soft-delete (trash) idempotency, mirroring the restore fix above ──────────────────

    /// <summary>Repository layer: SoftDeleteAsync called a SECOND time directly against a row
    /// that is ALREADY trashed (no ItemService pre-read guard involved — called straight on the
    /// repository) must report false: no row matches the atomic "deletedat IS NULL" guard, so neither
    /// DeletedAt nor Version is touched again. Before the fix, SoftDeleteGenericAsync's UPDATE located
    /// the row by id alone (Updateable&lt;T&gt; ignores the soft-delete query filter) and re-stamped/
    /// re-versioned unconditionally, which is exactly the race two concurrent DELETEs of the same live
    /// row could exploit to double-record a "delete" revision. Mirrors
    /// RestoreAsync_on_a_never_trashed_row_returns_false_and_does_not_bump_version above.</summary>
    [Fact]
    public async Task SoftDeleteAsync_on_an_already_trashed_row_returns_false_and_does_not_bump_version()
    {
        using var h = SoftDeleteRepositoryHarness.Create();
        var id = await h.InsertArticleAsync(status: "published");

        var firstDeleted = await h.Repository.SoftDeleteAsync(
            "fxArticle", id.ToString(), DateTime.UtcNow, null, default);
        Assert.True(firstDeleted);

        var afterFirst = (AuditableEntity)(await h.Repository.GetByIdAsync(
            "fxArticle", id.ToString(), DeletedFilter.With, default))!;
        var deletedAtAfterFirst = ((ISoftDeletable)afterFirst).DeletedAt;

        // Second SoftDeleteAsync call directly on the repository (bypassing ItemService entirely)
        // against the now-already-trashed row must be a true no-op at the SQL level.
        var secondDeleted = await h.Repository.SoftDeleteAsync(
            "fxArticle", id.ToString(), DateTime.UtcNow, null, default);
        Assert.False(secondDeleted);

        var afterSecond = (AuditableEntity)(await h.Repository.GetByIdAsync(
            "fxArticle", id.ToString(), DeletedFilter.With, default))!;
        Assert.Equal(afterFirst.Version, afterSecond.Version);
        Assert.Equal(deletedAtAfterFirst, ((ISoftDeletable)afterSecond).DeletedAt);
    }

    // ── Engine-level exclusion regressions ────

    [Fact]
    public async Task Deep_expansion_excludes_a_trashed_m2o_parent()
    {
        using var h = SoftDeleteRepositoryHarness.Create();
        var categoryId = await h.InsertCategoryAsync("WillBeTrashed");
        var articleId = await h.InsertArticleWithCategoryAsync(categoryId, status: "published");
        await h.Repository.SoftDeleteAsync("fxCategory", categoryId.ToString(), DateTime.UtcNow, null, default);

        var deep = new DeepSpec(new Dictionary<string, DeepRelationSpec>(StringComparer.OrdinalIgnoreCase)
        {
            ["category"] = new DeepRelationSpec(null, null)
        });

        var row = await h.Service.GetAsync(
            "fxArticle", articleId.ToString(), deep, null, DeletedFilter.Exclude, default);

        Assert.NotNull(row);                          // the article itself is still readable
        Assert.True(row!.ContainsKey("category"));
        Assert.Null(row["category"]);                  // trashed parent is not expanded
    }

    // The M2M existence check excluding a trashed target is exercised end-to-end via
    // Service.UpdateAsync's SyncM2MAsync, which validates target ids through
    // IItemRepository.QueryWhereInAsync(targetCollection, "id", ids). The fixture domain's only M2M
    // relation is FxArticle.Tags -> FxTag, and FxTag does NOT implement ISoftDeletable (see FxTag.cs) — so a
    // real "link a trashed tag" scenario can't surface the soft-delete floor at all (FxTag rows are
    // never filtered regardless of DeletedAt, because it has no DeletedAt). No other M2M relation onto
    // a soft-deletable collection (FxArticle/FxCategory) exists in this fixture domain. This test instead
    // proves the underlying primitive the existence check depends on: QueryWhereInAsync excludes a
    // trashed row of a collection that IS soft-deletable (FxCategory), which is exactly the mechanism
    // that would reject a trashed M2M target if/when a soft-deletable M2M target collection exists.
    [Fact]
    public async Task QueryWhereIn_excludes_a_trashed_row_of_a_soft_deletable_collection()
    {
        using var h = SoftDeleteRepositoryHarness.Create();
        var categoryId = await h.InsertCategoryAsync("TrashedTarget");
        await h.Repository.SoftDeleteAsync("fxCategory", categoryId.ToString(), DateTime.UtcNow, null, default);

        var found = await h.Repository.QueryWhereInAsync("fxCategory", "id", [categoryId], default);
        Assert.Empty(found);
    }

    // The claim that inbound-Restrict counts live references only is NOT exercisable with the
    // current fixture entities: FxArticle.CategoryId is OnDelete.SetNull and FxCategory.ParentId
    // (self-reference) is also OnDelete.SetNull (see FxArticle.cs/FxCategory.cs) — no relation in the
    // fixture domain uses OnDelete.Restrict, so graph.InboundRestrict() is empty for every
    // soft-deletable collection and ItemService.DeleteAsync's Restrict-guard branch never runs for
    // them. Deferred to the live gate / a future fixture entity with a Restrict relation; no test added
    // here to avoid a fabricated pass that doesn't actually exercise Restrict.

    // ── Floor-locking negative tests ──────────

    [Fact]
    public async Task Non_soft_deletable_collection_ignores_the_deleted_filter_entirely()
    {
        using var h = SoftDeleteRepositoryHarness.Create();
        var t1 = (FxTag)await h.Repository.CreateAsync("fxTag", new FxTag { Name = "Alpha" });
        var t2 = (FxTag)await h.Repository.CreateAsync("fxTag", new FxTag { Name = "Beta" });

        // FxTag does not implement ISoftDeletable, so the global <ISoftDeletable> table filter never
        // attaches to it (SqlSugar's AddTableFilter<T> only applies to types assignable to T), and the
        // Only/With DeletedFilter branches in RunQueryAsync are gated on `isSoftDeletable`. Exclude and
        // Only must therefore both see the full, unfiltered set for a non-soft-deletable collection.
        var excluded = await h.Repository.QueryAsync("fxTag",
            new QueryModel(null, null, [], 100, 0, null), [], null, DeletedFilter.Exclude, default);
        var only = await h.Repository.QueryAsync("fxTag",
            new QueryModel(null, null, [], 100, 0, null), [], null, DeletedFilter.Only, default);

        excluded.Rows.Select(SoftDeleteRepositoryHarness.IdOf).Should().BeEquivalentTo([t1.Id, t2.Id]);
        only.Rows.Select(SoftDeleteRepositoryHarness.IdOf).Should().BeEquivalentTo([t1.Id, t2.Id]);
    }

    [Fact]
    public async Task Deleted_filter_scoping_is_per_query_not_sticky_on_the_shared_client()
    {
        using var h = SoftDeleteRepositoryHarness.Create();
        var live = await h.InsertArticleAsync(status: "published");
        var trashed = await h.InsertArticleAsync(status: "published");
        await h.Repository.SoftDeleteAsync("fxArticle", trashed.ToString(), DateTime.UtcNow, null, default);

        // First call on this client lifts the floor via ClearFilter<ISoftDeletable>() (DeletedFilter.With).
        var with = await h.Repository.QueryAsync("fxArticle",
            new QueryModel(null, null, [], 100, 0, null), [], null, DeletedFilter.With, default);
        with.Rows.Select(SoftDeleteRepositoryHarness.IdOf).Should().Contain([live, trashed]);

        // A SUBSEQUENT call on the SAME ISqlSugarClient with Exclude must still filter —
        // proving ClearFilter is applied fresh per Queryable<T>() call (NewQueryable()), not left
        // dangling/sticky on the shared client from the prior With call.
        var excludeAgain = await h.Repository.QueryAsync("fxArticle",
            new QueryModel(null, null, [], 100, 0, null), [], null, DeletedFilter.Exclude, default);
        var idsAgain = excludeAgain.Rows.Select(SoftDeleteRepositoryHarness.IdOf).ToList();
        idsAgain.Should().Contain(live);
        idsAgain.Should().NotContain(trashed);
    }
}

/// <summary>
/// Mirrors the client/repository construction in <see cref="SqlSugarItemRepositoryTests"/>, but
/// builds the client via <see cref="SqlSugarClientFactory.Create"/> so the global
/// soft-delete filter is actually registered on the client under test.
/// </summary>
internal sealed class SoftDeleteRepositoryHarness : IDisposable
{
    private static readonly Guid Tester = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");

    private readonly ITestDatabase _database;
    private readonly ISqlSugarClient _db;

    public IItemRepository Repository { get; }
    public ItemService Service { get; }

    private SoftDeleteRepositoryHarness(
        ITestDatabase database, ISqlSugarClient db, IItemRepository repo, ItemService service)
    {
        _database = database;
        _db = db;
        Repository = repo;
        Service = service;
    }

    public static SoftDeleteRepositoryHarness Create(Guid? currentUserId = null)
    {
        var database = TestBackend.CreateUnitDatabase();
        var currentUser = new TestCurrentUserAccessor(currentUserId ?? Tester);
        var db = SqlSugarClientFactory.Create(
            database.Options(),
            currentUser);
        db.CodeFirst.InitTables<FxArticle>();
        db.CodeFirst.InitTables<FxArticleTranslation>();
        db.CodeFirst.InitTables<Language>();
        db.CodeFirst.InitTables<FxCategory>();
        db.CodeFirst.InitTables<FxTag>();
        db.CodeFirst.InitTables<FxArticleTag>();
        db.CodeFirst.InitTables<Revision>();
        LanguageSeeder.SeedAsync(db, TestLocalization.Default).GetAwaiter().GetResult();

        var collections = MetadataScanner.ScanTypes(
            [typeof(FxArticle), typeof(FxCategory), typeof(FxTag), typeof(Struo.Infrastructure.Files.File),
             typeof(Struo.Infrastructure.Files.MediaFolder)]);
        var provider = new CachedMetadataProvider(collections);
        var descriptors = MetadataScanner.ScanDescriptors([typeof(FxArticle), typeof(FxCategory), typeof(FxTag)]);
        var registry = new EntityRegistry(descriptors);
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
        var revisionStore = new SqlSugarRevisionStore(db, currentUser);
        var snapshotBuilder = new RevisionSnapshotBuilder(repo, provider, registry, graph);
        var service = TestItemService.Create(
            repo,
            provider,
            registry,
            graph,
            expander,
            graph,
            languages,
            currentUser,
            revisionStore,
            snapshotBuilder);

        return new SoftDeleteRepositoryHarness(database, db, repo, service);
    }

    public async Task<Guid> InsertArticleAsync(string status)
    {
        var created = (FxArticle)await Repository.CreateAsync("fxArticle", new FxArticle { Status = status });
        return created.Id;
    }

    public async Task<Guid> InsertCategoryAsync(string name)
    {
        var created = (FxCategory)await Repository.CreateAsync("fxCategory", new FxCategory { Name = name });
        return created.Id;
    }

    public async Task<Guid> InsertArticleWithCategoryAsync(Guid categoryId, string status)
    {
        var created = (FxArticle)await Repository.CreateAsync(
            "fxArticle", new FxArticle { Status = status, CategoryId = categoryId });
        return created.Id;
    }

    public Task<int> SoftDeleteRawAsync(Guid id) =>
        _db.Updateable<FxArticle>()
            .SetColumns(a => a.DeletedAt == DateTime.UtcNow)
            .Where(a => a.Id == id)
            .ExecuteCommandAsync();

    public static Guid IdOf(object row) => (Guid)row.GetType().GetProperty("Id")!.GetValue(row)!;

    public void Dispose() => _database.Dispose();
}
