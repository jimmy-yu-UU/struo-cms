// tests/Struo.Tests/Query/SoftDeleteRepositoryTests.cs
using AwesomeAssertions;
using SqlSugar;
using Struo.Application.Configuration;
using Struo.Application.Query;
using Struo.Domain.Query;
using Struo.Infrastructure.Metadata;
using Struo.Infrastructure.Persistence;
using Struo.Infrastructure.Query;
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

    private SoftDeleteRepositoryHarness(SqliteTestDatabase file, ISqlSugarClient db, IItemRepository repo)
    {
        _file = file;
        _db = db;
        Repository = repo;
    }

    public static SoftDeleteRepositoryHarness Create()
    {
        var file = new SqliteTestDatabase();
        var db = SqlSugarClientFactory.Create(
            new DatabaseOptions { DbType = StruoDbType.Sqlite, ConnectionString = file.ConnectionString },
            new TestCurrentUserAccessor(Tester));
        db.CodeFirst.InitTables<Article>();
        db.CodeFirst.InitTables<Category>();

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

        return new SoftDeleteRepositoryHarness(file, db, repo);
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
