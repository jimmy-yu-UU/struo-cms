// tests/Struo.Tests/Query/SoftDeleteRepositoryTests.cs
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
using Struo.Infrastructure.Security;
using Struo.Sample.Blog;
using Struo.Tests.Support;
using Xunit;

namespace Struo.Tests.Query;

// Phase 9b, Task 4: the global query filter registered in SqlSugarClientFactory must exclude
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

        var result = await h.Repository.QueryAsync("article",
            new QueryModel(null, null, [], 100, 0, null), [], null, default);

        var ids = result.Rows.Select(h.IdOf).ToList();
        ids.Should().Contain(live);
        ids.Should().NotContain(trashed);
    }

    [Fact]
    public async Task Query_with_Only_returns_just_trashed()
    {
        using var h = SoftDeleteRepositoryHarness.Create();
        var live = await h.InsertArticleAsync(status: "published");
        var trashed = await h.InsertArticleAsync(status: "published");
        await h.Repository.SoftDeleteAsync("article", trashed.ToString(), DateTime.UtcNow, null, default);

        var only = await h.Repository.QueryAsync("article",
            new QueryModel(null, null, [], 100, 0, null), [], null, DeletedFilter.Only, default);
        var ids = only.Rows.Select(h.IdOf).ToList();
        Assert.Contains(trashed, ids);
        Assert.DoesNotContain(live, ids);
    }

    [Fact]
    public async Task Restore_makes_row_visible_again()
    {
        using var h = SoftDeleteRepositoryHarness.Create();
        var id = await h.InsertArticleAsync(status: "published");
        await h.Repository.SoftDeleteAsync("article", id.ToString(), DateTime.UtcNow, null, default);
        Assert.Null(await h.Repository.GetByIdAsync("article", id.ToString(), DeletedFilter.Exclude, default));

        var restored = await h.Repository.RestoreAsync("article", id.ToString(), default);
        Assert.True(restored);
        Assert.NotNull(await h.Repository.GetByIdAsync("article", id.ToString(), DeletedFilter.Exclude, default));
    }

    [Fact]
    public async Task GetById_with_With_finds_trashed_row()
    {
        using var h = SoftDeleteRepositoryHarness.Create();
        var id = await h.InsertArticleAsync(status: "published");
        await h.Repository.SoftDeleteAsync("article", id.ToString(), DateTime.UtcNow, null, default);
        Assert.NotNull(await h.Repository.GetByIdAsync("article", id.ToString(), DeletedFilter.With, default));
    }

    // ── Task 6: ItemService soft-delete/purge/restore end-to-end ───────────────

    private static readonly Guid KnownUser = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");

    [Fact]
    public async Task Delete_soft_deletes_and_stamps_actor()
    {
        using var h = SoftDeleteRepositoryHarness.Create(currentUserId: KnownUser);
        var id = await h.InsertArticleAsync(status: "published");

        Assert.True(await h.Service.DeleteAsync("article", id.ToString(), purge: false, default));

        Assert.Null(await h.Service.GetAsync("article", id.ToString(), null, null, DeletedFilter.Exclude, default));
        var trashed = await h.Repository.GetByIdAsync("article", id.ToString(), DeletedFilter.With, default);
        Assert.NotNull(trashed);
        Assert.Equal(KnownUser, ((ISoftDeletable)trashed!).DeletedBy);
    }

    [Fact]
    public async Task Purge_hard_deletes_a_soft_delete_collection()
    {
        using var h = SoftDeleteRepositoryHarness.Create();
        var id = await h.InsertArticleAsync(status: "published");
        await h.Service.DeleteAsync("article", id.ToString(), purge: false, default);   // soft
        Assert.True(await h.Service.DeleteAsync("article", id.ToString(), purge: true, default)); // purge
        Assert.Null(await h.Repository.GetByIdAsync("article", id.ToString(), DeletedFilter.With, default));
    }

    [Fact]
    public async Task Restore_clears_marker_via_service()
    {
        using var h = SoftDeleteRepositoryHarness.Create();
        var id = await h.InsertArticleAsync(status: "published");
        await h.Service.DeleteAsync("article", id.ToString(), purge: false, default);
        var restored = await h.Service.RestoreAsync("article", id.ToString(), default);
        Assert.NotNull(restored);
        Assert.NotNull(await h.Service.GetAsync("article", id.ToString(), null, null, DeletedFilter.Exclude, default));
    }

    [Fact]
    public async Task Restore_unknown_id_returns_null()
    {
        using var h = SoftDeleteRepositoryHarness.Create();
        Assert.Null(await h.Service.RestoreAsync("article", Guid.NewGuid().ToString(), default));
    }

    // ── Final-review fix (Important #1): engine-level exclusion regressions ────

    [Fact]
    public async Task Deep_expansion_excludes_a_trashed_m2o_parent()
    {
        using var h = SoftDeleteRepositoryHarness.Create();
        var categoryId = await h.InsertCategoryAsync("WillBeTrashed");
        var articleId = await h.InsertArticleWithCategoryAsync(categoryId, status: "published");
        await h.Repository.SoftDeleteAsync("category", categoryId.ToString(), DateTime.UtcNow, null, default);

        var deep = new DeepSpec(new Dictionary<string, DeepRelationSpec>(StringComparer.OrdinalIgnoreCase)
        {
            ["category"] = new DeepRelationSpec(null, null)
        });

        var row = await h.Service.GetAsync(
            "article", articleId.ToString(), deep, null, DeletedFilter.Exclude, default);

        Assert.NotNull(row);                          // the article itself is still readable
        Assert.True(row!.ContainsKey("category"));
        Assert.Null(row["category"]);                  // trashed parent is not expanded
    }

    // Spec §9's "M2M existence check excludes a trashed target" claim is exercised end-to-end via
    // Service.UpdateAsync's SyncM2MAsync, which validates target ids through
    // IItemRepository.QueryWhereInAsync(targetCollection, "id", ids). The sample domain's only M2M
    // relation is Article.Tags -> Tag, and Tag does NOT implement ISoftDeletable (see Tag.cs) — so a
    // real "link a trashed tag" scenario can't surface the soft-delete floor at all (Tag rows are
    // never filtered regardless of DeletedAt, because it has no DeletedAt). No other M2M relation onto
    // a soft-deletable collection (Article/Category) exists in this sample domain. This test instead
    // proves the underlying primitive the existence check depends on: QueryWhereInAsync excludes a
    // trashed row of a collection that IS soft-deletable (Category), which is exactly the mechanism
    // that would reject a trashed M2M target if/when a soft-deletable M2M target collection exists.
    [Fact]
    public async Task QueryWhereIn_excludes_a_trashed_row_of_a_soft_deletable_collection()
    {
        using var h = SoftDeleteRepositoryHarness.Create();
        var categoryId = await h.InsertCategoryAsync("TrashedTarget");
        await h.Repository.SoftDeleteAsync("category", categoryId.ToString(), DateTime.UtcNow, null, default);

        var found = await h.Repository.QueryWhereInAsync("category", "id", [categoryId], default);
        Assert.Empty(found);
    }

    // Spec §9's "Inbound-Restrict counts live references only" claim is NOT exercisable with the
    // current sample entities: Article.CategoryId is OnDelete.SetNull and Category.ParentId
    // (self-reference) is also OnDelete.SetNull (see Article.cs/Category.cs) — no relation in the
    // sample domain uses OnDelete.Restrict, so graph.InboundRestrict() is empty for every
    // soft-deletable collection and ItemService.DeleteAsync's Restrict-guard branch never runs for
    // them. Deferred to the live gate / a future sample entity with a Restrict relation; no test added
    // here to avoid a fabricated pass that doesn't actually exercise Restrict.

    // ── Final-review fix (Important #1): floor-locking negative tests ──────────

    [Fact]
    public async Task Non_soft_deletable_collection_ignores_the_deleted_filter_entirely()
    {
        using var h = SoftDeleteRepositoryHarness.Create();
        var t1 = (Tag)await h.Repository.CreateAsync("tag", new Tag { Name = "Alpha" });
        var t2 = (Tag)await h.Repository.CreateAsync("tag", new Tag { Name = "Beta" });

        // Tag does not implement ISoftDeletable, so the global <ISoftDeletable> table filter never
        // attaches to it (SqlSugar's AddTableFilter<T> only applies to types assignable to T), and the
        // Only/With DeletedFilter branches in RunQueryAsync are gated on `isSoftDeletable`. Exclude and
        // Only must therefore both see the full, unfiltered set for a non-soft-deletable collection.
        var excluded = await h.Repository.QueryAsync("tag",
            new QueryModel(null, null, [], 100, 0, null), [], null, DeletedFilter.Exclude, default);
        var only = await h.Repository.QueryAsync("tag",
            new QueryModel(null, null, [], 100, 0, null), [], null, DeletedFilter.Only, default);

        excluded.Rows.Select(h.IdOf).Should().BeEquivalentTo([t1.Id, t2.Id]);
        only.Rows.Select(h.IdOf).Should().BeEquivalentTo([t1.Id, t2.Id]);
    }

    [Fact]
    public async Task Deleted_filter_scoping_is_per_query_not_sticky_on_the_scoped_client()
    {
        using var h = SoftDeleteRepositoryHarness.Create();
        var live = await h.InsertArticleAsync(status: "published");
        var trashed = await h.InsertArticleAsync(status: "published");
        await h.Repository.SoftDeleteAsync("article", trashed.ToString(), DateTime.UtcNow, null, default);

        // First call on this client lifts the floor via ClearFilter<ISoftDeletable>() (DeletedFilter.With).
        var with = await h.Repository.QueryAsync("article",
            new QueryModel(null, null, [], 100, 0, null), [], null, DeletedFilter.With, default);
        with.Rows.Select(h.IdOf).Should().Contain([live, trashed]);

        // A SUBSEQUENT call on the SAME client/scoped ISqlSugarClient with Exclude must still filter —
        // proving ClearFilter is applied fresh per Queryable<T>() call (NewQueryable()), not left
        // dangling/sticky on the shared scoped client from the prior With call.
        var excludeAgain = await h.Repository.QueryAsync("article",
            new QueryModel(null, null, [], 100, 0, null), [], null, DeletedFilter.Exclude, default);
        var idsAgain = excludeAgain.Rows.Select(h.IdOf).ToList();
        idsAgain.Should().Contain(live);
        idsAgain.Should().NotContain(trashed);
    }
}

/// <summary>
/// Mirrors the client/repository construction in <see cref="SqlSugarItemRepositoryTests"/>, but
/// builds the client via <see cref="SqlSugarClientFactory.Create"/> so the Phase 9b global
/// soft-delete filter is actually registered on the scoped client under test.
/// </summary>
internal sealed class SoftDeleteRepositoryHarness : IDisposable
{
    private static readonly Guid Tester = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");

    private readonly SqliteTestDatabase _file;
    private readonly ISqlSugarClient _db;

    public IItemRepository Repository { get; }
    public ItemService Service { get; }

    private SoftDeleteRepositoryHarness(
        SqliteTestDatabase file, ISqlSugarClient db, IItemRepository repo, ItemService service)
    {
        _file = file;
        _db = db;
        Repository = repo;
        Service = service;
    }

    public static SoftDeleteRepositoryHarness Create(Guid? currentUserId = null)
    {
        var file = new SqliteTestDatabase();
        var currentUser = new TestCurrentUserAccessor(currentUserId ?? Tester);
        var db = SqlSugarClientFactory.Create(
            new DatabaseOptions { DbType = StruoDbType.Sqlite, ConnectionString = file.ConnectionString },
            currentUser);
        db.CodeFirst.InitTables<Article>();
        db.CodeFirst.InitTables<ArticleTranslation>();
        db.CodeFirst.InitTables<Language>();
        db.CodeFirst.InitTables<Category>();
        db.CodeFirst.InitTables<Tag>();
        LanguageSeeder.SeedAsync(db).GetAwaiter().GetResult();

        var collections = MetadataScanner.ScanTypes(
            [typeof(Article), typeof(Category), typeof(Tag), typeof(Struo.Infrastructure.Files.File)]);
        var provider = new CachedMetadataProvider(collections);
        var descriptors = MetadataScanner.ScanDescriptors([typeof(Article), typeof(Category), typeof(Tag)]);
        var registry = new EntityRegistry(descriptors);
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
        var service = new ItemService(repo, provider, registry, new AllowAllPermissionService(),
            graph, expander, graph, resolver, languages, new StruoQueryOptions(), new GanssHtmlSanitizer(),
            currentUser);

        return new SoftDeleteRepositoryHarness(file, db, repo, service);
    }

    public async Task<Guid> InsertArticleAsync(string status)
    {
        var created = (Article)await Repository.CreateAsync("article", new Article { Status = status });
        return created.Id;
    }

    public async Task<Guid> InsertCategoryAsync(string name)
    {
        var created = (Category)await Repository.CreateAsync("category", new Category { Name = name });
        return created.Id;
    }

    public async Task<Guid> InsertArticleWithCategoryAsync(Guid categoryId, string status)
    {
        var created = (Article)await Repository.CreateAsync(
            "article", new Article { Status = status, CategoryId = categoryId });
        return created.Id;
    }

    public Task<int> SoftDeleteRawAsync(Guid id) =>
        _db.Updateable<Article>()
            .SetColumns(a => a.DeletedAt == DateTime.UtcNow)
            .Where(a => a.Id == id)
            .ExecuteCommandAsync();

    public Guid IdOf(object row) => (Guid)row.GetType().GetProperty("Id")!.GetValue(row)!;

    public void Dispose() => _file.Dispose();
}
