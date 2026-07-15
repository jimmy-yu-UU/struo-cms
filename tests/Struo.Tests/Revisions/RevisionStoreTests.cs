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
    private readonly ISqlSugarClient _db;

    public IRevisionStore Store { get; }

    private RevisionStoreHarness()
    {
        _db = SqlSugarClientFactory.Create(
            new DatabaseOptions { DbType = StruoDbType.Sqlite, ConnectionString = _file.ConnectionString },
            new TestCurrentUserAccessor(Tester));
        _db.CodeFirst.InitTables(typeof(Revision));
        Store = new SqlSugarRevisionStore(_db, new TestCurrentUserAccessor(Tester));
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
}
