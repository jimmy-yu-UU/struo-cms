// tests/Struo.Tests/Query/SqlSugarItemRepositoryTests.cs
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

public class SqlSugarItemRepositoryTests : IDisposable
{
    private static readonly Guid Tester = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");

    private readonly SqliteTestDatabase _file = new();
    private readonly ISqlSugarClient _db;
    private readonly IItemRepository _repo;

    public SqlSugarItemRepositoryTests()
    {
        _db = SqlSugarClientFactory.Create(
            new DatabaseOptions { DbType = StruoDbType.Sqlite, ConnectionString = _file.ConnectionString },
            new TestCurrentUserAccessor(Tester));
        _db.CodeFirst.InitTables<Article>();
        var collections = MetadataScanner.ScanTypes(
            [typeof(Article), typeof(Category), typeof(Tag), typeof(Struo.Infrastructure.Files.File),
             typeof(Struo.Infrastructure.Files.MediaFolder)]);
        var provider = new CachedMetadataProvider(collections);
        var descriptors = MetadataScanner.ScanDescriptors(
            [typeof(Article), typeof(Category), typeof(Tag), typeof(Struo.Infrastructure.Localization.Language)]);
        var registry = new EntityRegistry(descriptors);
        var collectionTypes = new Dictionary<string, Type>(StringComparer.OrdinalIgnoreCase)
        {
            ["article"]  = typeof(Article),
            ["category"] = typeof(Category),
            ["tag"]      = typeof(Tag),
            ["file"]     = typeof(Struo.Infrastructure.Files.File),
            ["mediafolder"] = typeof(Struo.Infrastructure.Files.MediaFolder),
        };
        var graph = new RelationshipGraph(collections, collectionTypes);
        _repo = new SqlSugarItemRepository(_db, registry, graph, provider, new StruoQueryOptions());
    }

    public void Dispose() => _file.Dispose();

    // D5: a non-page-aligned offset returns the exact window (offset is absolute, not a page index).
    [Fact]
    public async Task Query_offset_returns_exact_window()
    {
        _db.CodeFirst.InitTables<Category>();
        for (var i = 0; i < 5; i++)
            await _repo.CreateAsync("category", new Category { Name = $"C{i}" });

        // sort by name asc, skip 1, take 2 → C1, C2 (would be wrong under page-index division).
        var q = new QueryModel(null, null, [new SortField("name", false)], 2, 1, null);
        var result = await _repo.QueryAsync("category", q, []);

        result.Total.Should().Be(5);
        result.Rows.Select(r => ((Category)r).Name).Should().Equal("C1", "C2");
    }

    // D1: aggregate-write atomicity.
    [Fact]
    public async Task InTransaction_rolls_back_on_throw()
    {
        _db.CodeFirst.InitTables<Category>();
        var act = async () => await _repo.InTransactionAsync(async () =>
        {
            await _repo.CreateAsync("category", new Category { Name = "Temp" });
            throw new InvalidOperationException("boom");
        });
        await act.Should().ThrowAsync<InvalidOperationException>();
        (await _db.Queryable<Category>().CountAsync()).Should().Be(0, "a throw must roll the insert back");
    }

    [Fact]
    public async Task InTransaction_commits_on_success()
    {
        _db.CodeFirst.InitTables<Category>();
        await _repo.InTransactionAsync(async () =>
            await _repo.CreateAsync("category", new Category { Name = "Keep" }));
        (await _db.Queryable<Category>().CountAsync()).Should().Be(1);
    }

    // D1: nesting-safety — an inner InTransactionAsync must join the outer one, not commit
    // independently, so an outer rollback also undoes the inner write.
    [Fact]
    public async Task Nested_InTransaction_rolls_back_with_outer()
    {
        _db.CodeFirst.InitTables<Category>();
        var act = async () => await _repo.InTransactionAsync(async () =>
        {
            await _repo.InTransactionAsync(async () =>
                await _repo.CreateAsync("category", new Category { Name = "Inner" }));
            throw new InvalidOperationException("outer boom");
        });
        await act.Should().ThrowAsync<InvalidOperationException>();
        (await _db.Queryable<Category>().CountAsync()).Should().Be(0, "inner write joined the outer tran");
    }

    [Fact]
    public async Task CreateAsync_assigns_a_version7_guid_id()
    {
        _db.CodeFirst.InitTables<Category>();
        var created = await _repo.CreateAsync("category", new Category { Name = "News" });

        var id = (Guid)created.GetType().GetProperty("Id")!.GetValue(created)!;
        id.Should().NotBe(Guid.Empty);
        id.Version.Should().Be(7);
    }

    [Fact]
    public async Task Create_then_get_returns_entity_with_audit()
    {
        var created = (Article)await _repo.CreateAsync("article", new Article { Status = "draft" });
        created.Id.Should().NotBe(Guid.Empty);

        var fetched = (Article?)await _repo.GetByIdAsync("article", created.Id.ToString());
        fetched.Should().NotBeNull();
        fetched!.Status.Should().Be("draft");
        fetched.CreatedBy.Should().Be(Tester);
    }

    [Fact]
    public async Task Query_filters_and_paginates()
    {
        for (var i = 0; i < 5; i++)
            await _repo.CreateAsync("article", new Article { Status = i % 2 == 0 ? "published" : "draft" });

        var q = new QueryModel(null,
            new ComparisonFilter("status", QueryOperator.Eq, "published"),
            [new SortField("status", false)], 2, 0, null);

        var result = await _repo.QueryAsync("article", q, []);
        result.Total.Should().Be(3);
        result.Rows.Should().HaveCount(2);
    }

    [Fact]
    public async Task Update_changes_fields_and_delete_removes()
    {
        var created = (Article)await _repo.CreateAsync("article", new Article { Status = "draft" });
        var updated = (Article?)await _repo.UpdateAsync("article", created.Id.ToString(),
            new Article { Status = "published" });
        updated!.Status.Should().Be("published");
        // Audit AOP must stamp UpdatedBy on the persisted row.
        updated.UpdatedBy.Should().Be(Tester);

        (await _repo.DeleteAsync("article", created.Id.ToString())).Should().BeTrue();
        (await _repo.GetByIdAsync("article", created.Id.ToString())).Should().BeNull();
    }

    // CS-2: a malformed id must surface as the mappable QueryException (-> HTTP 400), not a raw
    // FormatException (which the exception handler cannot map and masks as a 500).
    [Fact]
    public async Task GetByIdAsync_with_malformed_id_throws_QueryException_not_FormatException()
    {
        var act = async () => await _repo.GetByIdAsync("article", "not-a-guid");
        await act.Should().ThrowAsync<QueryException>();
    }

    [Fact]
    public async Task DeleteAsync_with_malformed_id_throws_QueryException_not_FormatException()
    {
        var act = async () => await _repo.DeleteAsync("article", "not-a-guid");
        await act.Should().ThrowAsync<QueryException>();
    }

    // CS-3: the by-id read path must forward the CancellationToken to the ORM query so an
    // already-cancelled request stops at the DB call instead of running to completion.
    [Fact]
    public async Task GetByIdAsync_honors_cancellation()
    {
        var created = (Article)await _repo.CreateAsync("article", new Article { Status = "draft" });
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var act = async () => await _repo.GetByIdAsync("article", created.Id.ToString(),
            DeletedFilter.Exclude, cts.Token);
        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    // CS-3: the create path must forward the CancellationToken to the ORM insert.
    [Fact]
    public async Task CreateAsync_honors_cancellation()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var act = async () => await _repo.CreateAsync("article", new Article { Status = "draft" }, cts.Token);
        await act.Should().ThrowAsync<OperationCanceledException>();
        // Cancellation must stop the write BEFORE it reaches the DB, not just report afterwards.
        // (Explicit None: SqlSugar keeps the last token on the scoped client's Ado, so a bare
        // CountAsync() would re-observe the cancelled token instead of counting.)
        (await _db.Queryable<Article>().CountAsync(CancellationToken.None)).Should().Be(0);
    }

    // CS-3 review fix: identity-PK collections (Language: long IsIdentity) get their id from the
    // DB, so the create path must back-populate it on the returned entity — ExecuteCommandAsync
    // alone would leave Id=0 and break the create response / GraphQL re-read.
    [Fact]
    public async Task CreateAsync_identity_pk_collection_back_populates_id()
    {
        _db.CodeFirst.InitTables<Struo.Infrastructure.Localization.Language>();
        var created = (Struo.Infrastructure.Localization.Language)await _repo.CreateAsync(
            "language", new Struo.Infrastructure.Localization.Language { Code = "en", Name = "English" });
        created.Id.Should().BeGreaterThan(0, "the DB-generated identity must be read back onto the entity");
        (await _repo.GetByIdAsync("language", created.Id.ToString())).Should().NotBeNull();
    }

    // CS-3 review fix: the identity-PK create branch calls ExecuteReturnEntityAsync (no ct overload
    // in SqlSugarCore 5.1.4.215), so it must observe an already-cancelled token pre-flight —
    // symmetric with the Guid path's ExecuteCommandAsync(ct) semantics.
    [Fact]
    public async Task CreateAsync_identity_pk_honors_cancellation()
    {
        _db.CodeFirst.InitTables<Struo.Infrastructure.Localization.Language>();
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var act = async () => await _repo.CreateAsync(
            "language", new Struo.Infrastructure.Localization.Language { Code = "en", Name = "English" }, cts.Token);
        await act.Should().ThrowAsync<OperationCanceledException>();
        // Cancellation must stop the write BEFORE it reaches the DB. (Explicit None: SqlSugar keeps
        // the last token on the scoped client's Ado, so a bare CountAsync() would re-observe the
        // cancelled token instead of counting.)
        (await _db.Queryable<Struo.Infrastructure.Localization.Language>().CountAsync(CancellationToken.None)).Should().Be(0);
    }
}
