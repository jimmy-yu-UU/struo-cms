// tests/Struo.Tests/Query/TrashedRestrictRepositoryTests.cs
using System.Text.Json;
using AwesomeAssertions;
using Struo.Application.Query;
using Struo.Application.Query.Write;
using Struo.Domain.Query;
using Struo.Infrastructure.Files;
using Xunit;

namespace Struo.Tests.Query;

/// <summary>
/// The repository primitives behind the trashed-row clean-up: a typed count and a set-null that match
/// trashed rows only, the nullability of a key column, and the locking read's transaction requirement.
/// </summary>
public sealed class TrashedRestrictRepositoryTests
{
    private const string Ref = "trashableRestrictRef";
    private static readonly object[] OneValue = [1];

    [Fact]
    public async Task Count_and_set_null_touch_trashed_rows_only()
    {
        using var h = PurgeIntegrityHarness.Create();
        var categoryId = Guid.Parse(await h.CreateCategoryAsync("Cat"));
        var live = Guid.Parse(await h.CreateTrashableRefAsync("Live", categoryId));
        var trashed = Guid.Parse(await h.CreateTrashableRefAsync("Trashed", categoryId));
        await h.Service.DeleteAsync(Ref, trashed.ToString());
        var liveBefore = (TrashableRestrictRef)(await h.Repository.GetByIdAsync(Ref, live.ToString()))!;

        var count = await h.Repository.CountTrashedWhereInAsync(Ref, "categoryId", [categoryId]);
        await h.Repository.SetForeignKeyNullForTrashedAsync(Ref, "categoryId", categoryId);

        count.Should().Be(1);
        var liveAfter = (TrashableRestrictRef)(await h.Repository.GetByIdAsync(Ref, live.ToString()))!;
        liveAfter.CategoryId.Should().Be(categoryId);
        liveAfter.Version.Should().Be(liveBefore.Version);
        var trashedAfter = (TrashableRestrictRef)(await h.Repository.GetByIdAsync(
            Ref, trashed.ToString(), DeletedFilter.With))!;
        trashedAfter.CategoryId.Should().BeNull();
    }

    [Fact]
    public async Task A_source_without_soft_delete_has_no_trashed_rows_and_is_left_alone()
    {
        using var h = PurgeIntegrityHarness.Create();
        var parent = Guid.Parse(await h.CreateFolderAsync("P"));
        var child = await h.Service.CreateAsync("mediafolder",
            JsonSerializer.SerializeToElement(new { name = "C", parentId = parent }));

        var count = await h.Repository.CountTrashedWhereInAsync("mediafolder", "parentId", [parent]);
        await h.Repository.SetForeignKeyNullForTrashedAsync("mediafolder", "parentId", parent);

        count.Should().Be(0);
        var stored = (MediaFolder)(await h.Repository.GetByIdAsync("mediafolder", child["id"]!.ToString()!))!;
        stored.ParentId.Should().Be(parent);
    }

    [Fact]
    public async Task Purging_a_folder_with_a_live_child_folder_is_refused_and_leaves_the_child_alone()
    {
        using var h = PurgeIntegrityHarness.Create();
        var parent = Guid.Parse(await h.CreateFolderAsync("P"));
        var child = await h.Service.CreateAsync("mediafolder",
            JsonSerializer.SerializeToElement(new { name = "C", parentId = parent }));

        await Assert.ThrowsAsync<RelationConflictException>(
            () => h.Service.DeleteAsync("mediafolder", parent.ToString(), purge: true));

        var stored = (MediaFolder)(await h.Repository.GetByIdAsync("mediafolder", child["id"]!.ToString()!))!;
        stored.ParentId.Should().Be(parent);
    }

    [Fact]
    public async Task A_locking_read_outside_a_transaction_throws()
    {
        using var h = PurgeIntegrityHarness.Create();
        var id = await h.CreateCategoryAsync("Cat");

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => h.Repository.GetByIdLockedAsync("fxCategory", id));
    }

    [Fact]
    public async Task A_locking_read_inside_a_transaction_returns_the_row()
    {
        using var h = PurgeIntegrityHarness.Create();
        var id = await h.CreateCategoryAsync("Cat");

        var row = await h.Repository.InTransactionAsync(
            () => h.Repository.GetByIdLockedAsync("fxCategory", id));

        row.Should().NotBeNull();
    }

    [Theory]
    [InlineData("trashableRestrictRef", "categoryId", true)]
    [InlineData("trashableRestrictRefRequired", "categoryId", false)]
    public void The_key_column_nullability_comes_from_the_column_metadata(string collection, string key, bool expected)
    {
        using var h = PurgeIntegrityHarness.Create();

        h.Repository.IsForeignKeyNullable(collection, key).Should().Be(expected);
    }

    // A repository wrapper that forwards only the members it knows must say so for the ones it does not,
    // not read without a lock or report a key as nullable on its own.
    [Fact]
    public async Task A_repository_that_does_not_implement_the_primitives_refuses_them()
    {
        IItemRepository bare = new BareRepository();

        await Assert.ThrowsAsync<NotSupportedException>(() => bare.GetByIdLockedAsync("c", "1"));
        await Assert.ThrowsAsync<NotSupportedException>(() => bare.CountTrashedWhereInAsync("c", "p", OneValue));
        await Assert.ThrowsAsync<NotSupportedException>(() => bare.SetForeignKeyNullForTrashedAsync("c", "p", 1));
        Assert.Throws<NotSupportedException>(() => bare.IsForeignKeyNullable("c", "p"));
    }

    private sealed class BareRepository : IItemRepository
    {
        public Task<QueryResult> QueryAsync(string c, QueryModel q, IReadOnlyList<string> s, string? l, DeletedFilter d, CancellationToken ct) => throw new NotImplementedException();
        public Task<object?> GetByIdAsync(string c, string id, DeletedFilter d, CancellationToken ct) => throw new NotImplementedException();
        public Task<object> CreateAsync(string c, object e, CancellationToken ct) => throw new NotImplementedException();
        public Task<object?> UpdateAsync(string c, string id, object e, CancellationToken ct) => throw new NotImplementedException();
        public Task<bool> DeleteAsync(string c, string id, CancellationToken ct) => throw new NotImplementedException();
        public Task<bool> SoftDeleteAsync(string c, string id, DateTime at, Guid? by, CancellationToken ct) => throw new NotImplementedException();
        public Task<bool> RestoreAsync(string c, string id, CancellationToken ct) => throw new NotImplementedException();
        public Task InTransactionAsync(Func<Task> body, CancellationToken ct) => throw new NotImplementedException();
        public Task<T> InTransactionAsync<T>(Func<Task<T>> body, CancellationToken ct) => throw new NotImplementedException();
        public Task<IReadOnlyList<object>> QueryWhereInAsync(string c, string p, IReadOnlyList<object> v, CancellationToken ct) => throw new NotImplementedException();
        public Task<IReadOnlyList<object>> QueryWhereInFilteredAsync(string c, string p, IReadOnlyList<object> v, FilterNode? f, string? l, int? m, CancellationToken ct) => throw new NotImplementedException();
        public Task<IReadOnlyList<object>> QueryEntityWhereInAsync(Type t, string p, IReadOnlyList<object> v, int? m, CancellationToken ct) => throw new NotImplementedException();
        public Task SyncManyToManyAsync(Type jt, string pfk, string tfk, string? sort, object pid, IReadOnlyList<JunctionLink> links, CancellationToken ct) => throw new NotImplementedException();
        public Task<IReadOnlyList<object>> LoadTranslationsAsync(Type tt, string fk, string lp, IReadOnlyList<object> pids, string? locale, CancellationToken ct) => throw new NotImplementedException();
        public Task InsertTranslationsAsync(Type tt, string fk, string lp, IReadOnlyList<string> fp, object pid, IReadOnlyDictionary<string, IReadOnlyDictionary<string, object?>> pl, CancellationToken ct) => throw new NotImplementedException();
        public Task SyncTranslationsAsync(TranslationSyncRequest request, CancellationToken ct = default) => throw new NotImplementedException();
    }
}
