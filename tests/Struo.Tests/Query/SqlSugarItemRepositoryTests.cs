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
    private readonly SqliteTestDatabase _file = new();
    private readonly ISqlSugarClient _db;
    private readonly IItemRepository _repo;

    public SqlSugarItemRepositoryTests()
    {
        _db = SqlSugarClientFactory.Create(
            new DatabaseOptions { DbType = StruoDbType.Sqlite, ConnectionString = _file.ConnectionString },
            new TestCurrentUserAccessor("tester"));
        _db.CodeFirst.InitTables<Article>();
        var collections = MetadataScanner.Scan(typeof(Article).Assembly);
        var provider = new CachedMetadataProvider(collections);
        var descriptors = MetadataScanner.ScanDescriptors([typeof(Article)]);
        var registry = new EntityRegistry(descriptors);
        var collectionTypes = new Dictionary<string, Type>(StringComparer.OrdinalIgnoreCase)
        {
            ["article"]  = typeof(Article),
            ["tag"]      = typeof(Tag),
            ["author"]   = typeof(Author),
            ["category"] = typeof(Category),
        };
        var graph = new RelationshipGraph(collections, collectionTypes);
        _repo = new SqlSugarItemRepository(_db, registry, graph, provider, new StruoQueryOptions());
    }

    public void Dispose() => _file.Dispose();

    [Fact]
    public async Task Create_then_get_returns_entity_with_audit()
    {
        var created = (Article)await _repo.CreateAsync("article", new Article { Status = "draft" });
        created.Id.Should().BeGreaterThan(0);

        var fetched = (Article?)await _repo.GetByIdAsync("article", created.Id.ToString());
        fetched.Should().NotBeNull();
        fetched!.Status.Should().Be("draft");
        fetched.CreatedBy.Should().Be("tester");
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
        updated.UpdatedBy.Should().Be("tester");

        (await _repo.DeleteAsync("article", created.Id.ToString())).Should().BeTrue();
        (await _repo.GetByIdAsync("article", created.Id.ToString())).Should().BeNull();
    }
}
