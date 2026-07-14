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

    public Task<int> SoftDeleteRawAsync(Guid id) =>
        _db.Updateable<Article>()
            .SetColumns(a => a.DeletedAt == DateTime.UtcNow)
            .Where(a => a.Id == id)
            .ExecuteCommandAsync();

    public Guid IdOf(object row) => (Guid)row.GetType().GetProperty("Id")!.GetValue(row)!;

    public void Dispose() => _file.Dispose();
}
