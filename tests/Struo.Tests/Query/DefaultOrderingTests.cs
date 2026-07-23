// tests/Struo.Tests/Query/DefaultOrderingTests.cs
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

public class DefaultOrderingTests : IDisposable
{
    private static readonly Guid Tester = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");

    private readonly SqliteTestDatabase _file = new();
    private readonly ISqlSugarClient _db;
    private readonly IItemRepository _repo;

    public DefaultOrderingTests()
    {
        _db = SqlSugarClientFactory.Create(
            new DatabaseOptions { DbType = StruoDbType.Sqlite, ConnectionString = _file.ConnectionString },
            new TestCurrentUserAccessor(Tester));
        _db.CodeFirst.InitTables<Category>();
        var collections = MetadataScanner.ScanTypes([typeof(Article), typeof(Category), typeof(Tag)]);
        var provider = new CachedMetadataProvider(collections);
        var descriptors = MetadataScanner.ScanDescriptors([typeof(Article), typeof(Category), typeof(Tag)]);
        var registry = new EntityRegistry(descriptors);
        var collectionTypes = new Dictionary<string, Type>(StringComparer.OrdinalIgnoreCase)
        {
            ["article"] = typeof(Article), ["category"] = typeof(Category), ["tag"] = typeof(Tag),
        };
        var graph = new RelationshipGraph(collections, collectionTypes);
        _repo = new SqlSugarItemRepository(_db, registry, graph, provider, new StruoQueryOptions());
    }

    public void Dispose() => _file.Dispose();

    // AuditAop (src/Struo.Infrastructure/Persistence/AuditAop.cs) unconditionally stamps CreatedAt/
    // UpdatedAt to DateTime.UtcNow on every insert of an IAuditable entity, so setting CreatedAt on
    // the object passed to Insertable is silently discarded. Insert first, then overwrite CreatedAt
    // via a partial SetColumns update (which the AOP does not intercept for CreatedAt) so tests can
    // control it directly, per the brief's NOTE.
    private async Task SeedWithCreatedAt(string name, DateTime createdAt)
    {
        var row = new Category { Id = Guid.CreateVersion7(), Name = name };
        await _db.Insertable(row).ExecuteCommandAsync();
        await _db.Updateable<Category>()
            .SetColumns(c => new Category { CreatedAt = createdAt, UpdatedAt = createdAt })
            .Where(c => c.Id == row.Id)
            .ExecuteCommandAsync();
    }

    // 問題 11: no client sort must still produce a deterministic, stable order (createdAt DESC, id ASC).
    [Fact]
    public async Task Query_without_sort_orders_by_createdAt_desc()
    {
        var t0 = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        await SeedWithCreatedAt("Old", t0);
        await SeedWithCreatedAt("Mid", t0.AddMinutes(1));
        await SeedWithCreatedAt("New", t0.AddMinutes(2));

        var q = new QueryModel(null, null, [], 10, 0, null);
        var result = await _repo.QueryAsync("category", q, []);

        result.Rows.Select(r => ((Category)r).Name).Should().Equal("New", "Mid", "Old");
    }

    // Updating a row must not change its list position (the PG heap-order symptom). Uses a direct
    // partial SqlSugar update (not IItemRepository.UpdateAsync, which clones/replaces the whole
    // entity and would zero out CreatedAt) so only Name/UpdatedAt change, proving the DEFAULT
    // ORDER BY (createdAt DESC, id ASC) is immune to the row's physical storage location moving.
    [Fact]
    public async Task Updating_a_row_keeps_its_default_order_position()
    {
        var t0 = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        await SeedWithCreatedAt("A", t0);
        await SeedWithCreatedAt("B", t0.AddMinutes(1));
        await SeedWithCreatedAt("C", t0.AddMinutes(2));

        var q = new QueryModel(null, null, [], 10, 0, null);
        var before = (await _repo.QueryAsync("category", q, [])).Rows.Select(r => ((Category)r).Id).ToList();

        var newest = (Category)(await _repo.QueryAsync("category", q, [])).Rows[0];
        await _db.Updateable<Category>()
            .SetColumns(c => new Category { Name = "C-renamed", UpdatedAt = DateTime.UtcNow })
            .Where(c => c.Id == newest.Id)
            .ExecuteCommandAsync();

        var after = (await _repo.QueryAsync("category", q, [])).Rows.Select(r => ((Category)r).Id).ToList();
        after.Should().Equal(before);
    }

    // Equal createdAt values fall back to id ASC — UUIDv7 is time-ordered, so ties stay deterministic.
    [Fact]
    public async Task Equal_createdAt_ties_break_on_id_ascending()
    {
        var t0 = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        await SeedWithCreatedAt("First", t0);
        await SeedWithCreatedAt("Second", t0);
        await SeedWithCreatedAt("Third", t0);

        var q = new QueryModel(null, null, [], 10, 0, null);
        var r1 = (await _repo.QueryAsync("category", q, [])).Rows.Select(r => ((Category)r).Id).ToList();
        var r2 = (await _repo.QueryAsync("category", q, [])).Rows.Select(r => ((Category)r).Id).ToList();

        r1.Should().Equal(r2);
        r1.Should().BeInAscendingOrder();
    }

    // An explicit client sort on a non-unique column gets the PK appended as tiebreak.
    [Fact]
    public async Task Explicit_sort_gets_id_tiebreak_for_stable_pagination()
    {
        var t0 = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        for (var i = 0; i < 4; i++)
            await SeedWithCreatedAt("Same", t0.AddSeconds(i));

        var q1 = new QueryModel(null, null, [new SortField("name", false)], 2, 0, null);
        var q2 = new QueryModel(null, null, [new SortField("name", false)], 2, 2, null);
        var page1 = (await _repo.QueryAsync("category", q1, [])).Rows.Select(r => ((Category)r).Id).ToList();
        var page2 = (await _repo.QueryAsync("category", q2, [])).Rows.Select(r => ((Category)r).Id).ToList();

        page1.Should().NotIntersectWith(page2, "an unstable sort would repeat/skip rows across pages");
        page1.Concat(page2).Should().OnlyHaveUniqueItems();
    }
}
