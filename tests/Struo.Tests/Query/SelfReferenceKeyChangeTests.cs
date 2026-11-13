// tests/Struo.Tests/Query/SelfReferenceKeyChangeTests.cs
using System.Data.Common;
using AwesomeAssertions;
using SqlSugar;
using Struo.Api.Http;
using Struo.Application.Query;
using Struo.Domain.Query;
using Struo.Infrastructure.Files;
using Xunit;

namespace Struo.Tests.Query;

/// <summary>
/// The cycle check locks the ancestor chain only when an update changes the self-reference key, decided
/// from the key stored in the database under the row's own lock. Also the answers of an update whose cycle
/// check follows the row write, and of a deadlock raised after the commit.
/// </summary>
public sealed class SelfReferenceKeyChangeTests : IDisposable
{
    private const string Folder = "mediafolder";
    private const string Plain = "plainNode";
    private readonly TreeHarness _h = new();

    public void Dispose() => _h.Dispose();

    private static Task<IReadOnlyDictionary<string, object?>?> Update(
        ItemService svc, string collection, Guid id, object body) =>
        svc.UpdateAsync(collection, id.ToString(), TreeHarness.Body(body));

    [Fact]
    public async Task An_update_that_keeps_the_parent_takes_one_locking_read_and_none_on_the_chain()
    {
        var a = await _h.CreateAsync(Folder, "A");
        var b = await _h.CreateAsync(Folder, "B", a);
        var c = await _h.CreateAsync(Folder, "C", b);
        var (svc, counting) = _h.Build();

        await Update(svc, Folder, c, new { name = "C2" });
        var afterRename = counting.LockedReads;
        counting.ResetCount();
        await Update(svc, Folder, c, new { name = "C3", parentId = b });

        afterRename.Should().Equal([(Folder, c.ToString())]);
        counting.LockedReads.Should().Equal([(Folder, c.ToString())]);
    }

    [Fact]
    public async Task An_update_that_changes_the_parent_locks_its_own_row_first_and_then_the_chain()
    {
        var w = await _h.CreateAsync(Folder, "W");
        var x = await _h.CreateAsync(Folder, "X", w);
        var c = await _h.CreateAsync(Folder, "C");
        var (svc, counting) = _h.Build();

        await Update(svc, Folder, c, new { parentId = x });

        counting.LockedReads.Select(r => r.Id).Should().Equal(c.ToString(), x.ToString(), w.ToString());
    }

    [Fact]
    public async Task An_update_of_a_collection_without_a_self_reference_takes_no_locking_read()
    {
        var leaf = await _h.CreateAsync("plainLeaf", "L");
        var (svc, counting) = _h.Build();

        await Update(svc, "plainLeaf", leaf, new { name = "L2" });

        counting.LockedReads.Should().BeEmpty();
    }

    [Fact]
    public async Task An_update_of_a_row_that_no_longer_exists_answers_not_found_without_walking_a_chain()
    {
        var a = await _h.CreateAsync(Folder, "A");
        var b = await _h.CreateAsync(Folder, "B", a);
        var (svc, counting) = _h.Build(interceptors: [new Interleave(Folder, async () =>
            await _h.Reader.Deleteable<MediaFolder>().In(b).ExecuteCommandAsync())]);

        var result = await Update(svc, Folder, b, new { parentId = (Guid?)null });

        result.Should().BeNull();
        counting.LockedReads.Should().Equal([(Folder, b.ToString())]);
    }

    // R is loaded under P. Before the write one request takes R out of the tree and another moves P under
    // R's child D. Writing R's old parent back would close the cycle P -> D -> R -> P.
    private async Task<(Guid R, ItemService Writer)> StaleSetupAsync(string collection)
    {
        var p = await _h.CreateAsync(collection, "P");
        var r = await _h.CreateAsync(collection, "R", p);
        var d = await _h.CreateAsync(collection, "D", r);
        var (writer, _) = _h.Build(interceptors: [new Interleave(collection, async () =>
        {
            await Update(_h.Setup, collection, r, new { parentId = (Guid?)null });
            await Update(_h.Setup, collection, p, new { parentId = d });
        })]);
        return (r, writer);
    }

    private static void AssertNoCycle(IReadOnlyList<(Guid Id, Guid? Parent)> rows)
    {
        var parentOf = rows.ToDictionary(r => r.Id, r => r.Parent);
        foreach (var row in rows)
        {
            var seen = new HashSet<Guid> { row.Id };
            for (var next = row.Parent; next is { } id; next = parentOf.GetValueOrDefault(id))
                seen.Add(id).Should().BeTrue("no row may be its own ancestor");
        }
    }

    [Fact]
    public async Task A_stale_write_to_a_versioned_row_is_a_version_conflict_and_leaves_no_cycle()
    {
        var (r, writer) = await StaleSetupAsync(Folder);

        await Assert.ThrowsAsync<ConcurrencyConflictException>(() => Update(writer, Folder, r, new { name = "R2" }));

        var rows = await _h.Reader.Queryable<MediaFolder>().ToListAsync();
        AssertNoCycle([.. rows.Select(x => (x.Id, x.ParentId))]);
    }

    // No version column, so the row write has no compare: the stored key differs from the one written, the
    // chain is walked under locks, and the cycle is refused.
    [Fact]
    public async Task A_stale_write_to_an_unversioned_row_is_refused_as_a_cycle_and_leaves_no_cycle()
    {
        var (r, writer) = await StaleSetupAsync(Plain);

        var ex = await Assert.ThrowsAsync<QueryException>(() => Update(writer, Plain, r, new { name = "R2" }));

        ex.Message.Should().Contain("cycle");
        var rows = await _h.Reader.Queryable<PlainNode>().ToListAsync();
        AssertNoCycle([.. rows.Select(x => (x.Id, x.ParentId))]);
        rows.Single(x => x.Id == r).ParentId.Should().BeNull();
    }

    // The row write hits the unique name first, so the cycle check is never reached.
    [Fact]
    public async Task A_unique_violation_wins_over_a_cycle_and_answers_conflict()
    {
        var a = await _h.CreateAsync(Plain, "A");
        var b = await _h.CreateAsync(Plain, "B", a);
        var (svc, _) = _h.Build();

        var ex = await Record.ExceptionAsync(() => Update(svc, Plain, a, new { name = "B", parentId = b }));

        ex.Should().NotBeNull();
        StruoExceptionHandler.Map(ex!, true).Status.Should().Be(409);
        ex.Should().NotBeOfType<QueryException>();
    }

    [Fact]
    public async Task A_deadlock_in_after_commit_work_answers_conflict_with_the_write_applied()
    {
        var leaf = await _h.CreateAsync("plainLeaf", "L");
        var (svc, _) = _h.Build(interceptors: [new ThrowAfterCommit()]);

        var ex = await Record.ExceptionAsync(() => Update(svc, "plainLeaf", leaf, new { name = "L2" }));

        ex.Should().NotBeNull();
        var (status, body) = StruoExceptionHandler.Map(ex!, true);
        status.Should().Be(409);
        body.Message.Should().Be(DomainErrorMap.DeadlockMessage);
        (await _h.Reader.Queryable<PlainLeaf>().In(leaf).FirstAsync()).Name.Should().Be("L2");
    }

    private sealed class Interleave(string collection, Func<Task> action) : IItemWriteInterceptor
    {
        private int _ran;

        public bool AppliesTo(string c) => string.Equals(c, collection, StringComparison.OrdinalIgnoreCase);

        public async Task BeforeWriteAsync(ItemWriteContext context, CancellationToken ct)
        {
            if (Interlocked.Exchange(ref _ran, 1) == 0) await action();
        }
    }

    private sealed class FakeDbException(string sqlState) : DbException("provider failure")
    {
        public override string? SqlState { get; } = sqlState;
    }

    private sealed class ThrowAfterCommit : IItemWriteInterceptor
    {
        public bool AppliesTo(string c) => string.Equals(c, "PlainLeaf", StringComparison.OrdinalIgnoreCase);

        public Task AfterCommitAsync(ItemWriteContext context, CancellationToken ct) =>
            throw new InvalidOperationException("after commit", new FakeDbException("40P01"));
    }
}
