// tests/Struo.Tests/Revisions/RevisionStoreTests.cs
using SqlSugar;
using Struo.Application.Configuration;
using Struo.Application.Revisions;
using Struo.Infrastructure.Persistence;
using Struo.Infrastructure.Revisions;
using Struo.Tests.Support;
using Xunit;

namespace Struo.Tests.Revisions;

/// <summary>Wires a SqlSugarRevisionStore over a throwaway SQLite file, mirroring
/// SqlSugarItemRepositoryTests' client-construction pattern.</summary>
public sealed class RevisionStoreHarness : IDisposable
{
    private static readonly Guid Tester = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");

    private readonly SqliteTestDatabase _file = new();

    public ISqlSugarClient Db { get; }
    public IRevisionStore Store { get; }

    private RevisionStoreHarness()
    {
        Db = SqlSugarClientFactory.Create(
            new DatabaseOptions { DbType = StruoDbType.Sqlite, ConnectionString = _file.ConnectionString },
            new TestCurrentUserAccessor(Tester));
        Db.CodeFirst.InitTables(typeof(Revision));
        Store = new SqlSugarRevisionStore(Db, new TestCurrentUserAccessor(Tester));
    }

    public static RevisionStoreHarness Create() => new();

    public void Dispose() => _file.Dispose();
}

public sealed class RevisionStoreTests
{
    [Fact]
    public async Task Capture_assigns_monotonic_per_item_numbers()
    {
        using var h = RevisionStoreHarness.Create();
        await h.Store.CaptureAsync("article", "itemA", "create", "{\"a\":1}", default);
        await h.Store.CaptureAsync("article", "itemA", "update", "{\"a\":2}", default);
        await h.Store.CaptureAsync("article", "itemB", "create", "{\"b\":1}", default); // separate item -> its own 1

        var a = await h.Store.ListAsync("article", "itemA", default);
        Assert.Equal([2L, 1L], a.Select(r => r.RevisionNumber).ToArray());   // newest-first
        Assert.Equal("update", a[0].Operation);

        var b = await h.Store.ListAsync("article", "itemB", default);
        Assert.Single(b);
        Assert.Equal(1L, b[0].RevisionNumber);                               // per-item sequence, not global
    }

    [Fact]
    public async Task Get_returns_snapshot_with_cjk_intact()
    {
        using var h = RevisionStoreHarness.Create();
        await h.Store.CaptureAsync("article", "x", "create", "{\"title\":\"人工智慧\"}", default);
        var rec = await h.Store.GetAsync("article", "x", 1, default);
        Assert.NotNull(rec);
        Assert.Contains("人工智慧", rec!.Snapshot, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Get_unknown_returns_null()
    {
        using var h = RevisionStoreHarness.Create();
        Assert.Null(await h.Store.GetAsync("article", "nope", 99, default));
    }

    // DB-4 (=CS-6): the composite UNIQUE index (collectionname, itemid, revisionnumber) is a backstop.
    // CaptureAsync's max()+1 already assigns distinct numbers under the ItemService write transaction;
    // the index guarantees a concurrent duplicate can never physically land. A direct duplicate insert
    // (simulating that race) must be rejected by the store below the ORM.
    [Fact]
    public async Task Duplicate_revision_number_for_same_item_is_rejected()
    {
        using var h = RevisionStoreHarness.Create();

        static Revision Row(long no, string op) => new()
        {
            Id = Guid.CreateVersion7(),
            CollectionName = "article",
            ItemId = "dup",
            RevisionNumber = no,
            Operation = op,
            Snapshot = "{}",
            CreatedAt = DateTime.UtcNow,
        };

        await h.Db.Insertable(Row(1, "create")).ExecuteCommandAsync();

        // Second physical row with the same (collectionname, itemid, revisionnumber) must violate the
        // unique index and throw — the backstop that makes a lost-update race fail closed.
        await Assert.ThrowsAnyAsync<Exception>(
            () => h.Db.Insertable(Row(1, "update")).ExecuteCommandAsync());
    }

    [Fact]
    public async Task Same_revision_number_is_allowed_across_different_items_and_collections()
    {
        using var h = RevisionStoreHarness.Create();

        static Revision Row(string collection, string item) => new()
        {
            Id = Guid.CreateVersion7(),
            CollectionName = collection,
            ItemId = item,
            RevisionNumber = 1,
            Operation = "create",
            Snapshot = "{}",
            CreatedAt = DateTime.UtcNow,
        };

        // The unique scope is the full triple — number 1 recurs per (collection,item), so none collide.
        await h.Db.Insertable(Row("article", "A")).ExecuteCommandAsync();
        await h.Db.Insertable(Row("article", "B")).ExecuteCommandAsync();
        await h.Db.Insertable(Row("category", "A")).ExecuteCommandAsync();

        var count = await h.Db.Queryable<Revision>().CountAsync();
        Assert.Equal(3, count);
    }
}
