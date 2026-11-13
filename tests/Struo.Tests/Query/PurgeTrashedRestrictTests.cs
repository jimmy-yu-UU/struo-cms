// tests/Struo.Tests/Query/PurgeTrashedRestrictTests.cs
using AwesomeAssertions;
using Struo.Application.Changes;
using Struo.Application.Query;
using Struo.Domain.Query;
using Struo.Tests.Support;
using Xunit;
using File = Struo.Infrastructure.Files.File;

namespace Struo.Tests.Query;

/// <summary>
/// A row in the trash that points through an OnDelete.Restrict relation at a purge target gets that key
/// set to null in the purge transaction, or the purge is refused (409) when the key cannot be null.
/// Either way no row, trashed or live, points at a target that no longer exists.
/// </summary>
public sealed class PurgeTrashedRestrictTests
{
    private const string NullableRef = "trashableRestrictRef";
    private const string RequiredRef = "trashableRestrictRefRequired";

    private static async Task<(Guid CategoryId, Guid RefId)> TrashedRefAsync(PurgeIntegrityHarness h)
    {
        var categoryId = Guid.Parse(await h.CreateCategoryAsync("Cat"));
        var refId = Guid.Parse(await h.CreateTrashableRefAsync("Ref", categoryId));
        (await h.Service.DeleteAsync(NullableRef, refId.ToString())).Should().BeTrue();
        return (categoryId, refId);
    }

    private static async Task<TrashableRestrictRef> StoredRefAsync(PurgeIntegrityHarness h, Guid id) =>
        (TrashableRestrictRef)(await h.Repository.GetByIdAsync(NullableRef, id.ToString(), DeletedFilter.With))!;

    [Fact]
    public async Task Nullable_key_of_a_trashed_row_is_cleared_and_the_row_restores_with_a_null_key()
    {
        using var h = PurgeIntegrityHarness.Create();
        var (categoryId, refId) = await TrashedRefAsync(h);
        var before = await StoredRefAsync(h, refId);

        var purged = await h.Service.DeleteAsync("fxCategory", categoryId.ToString(), purge: true);

        purged.Should().BeTrue();
        (await h.Repository.GetByIdAsync("fxCategory", categoryId.ToString(), DeletedFilter.With)).Should().BeNull();
        var after = await StoredRefAsync(h, refId);
        after.CategoryId.Should().BeNull();
        after.DeletedAt.Should().NotBeNull();
        after.Version.Should().Be(before.Version);
        after.UpdatedAt.Should().Be(before.UpdatedAt);
        after.UpdatedBy.Should().Be(before.UpdatedBy);

        (await h.Service.RestoreAsync(NullableRef, refId.ToString())).Should().NotBeNull();
        var live = (TrashableRestrictRef)(await h.Repository.GetByIdAsync(NullableRef, refId.ToString()))!;
        live.CategoryId.Should().BeNull();
    }

    [Fact]
    public async Task Clearing_a_trashed_row_raises_no_change_notification_for_it()
    {
        var listener = new RecordingItemChangeListener();
        using var h = PurgeIntegrityHarness.Create(
            notifier: new Struo.Infrastructure.Changes.ItemChangeNotifier(
                [listener], new ListLogger<Struo.Infrastructure.Changes.ItemChangeNotifier>()));
        var (categoryId, _) = await TrashedRefAsync(h);
        listener.Calls.Clear();

        await h.Service.DeleteAsync("fxCategory", categoryId.ToString(), purge: true);

        var change = listener.All.Should().ContainSingle().Subject;
        change.Kind.Should().Be(ItemChangeKind.Purged);
    }

    [Fact]
    public async Task Non_nullable_key_of_a_trashed_row_refuses_the_purge_with_a_message_naming_the_count()
    {
        using var h = PurgeIntegrityHarness.Create();
        var categoryId = Guid.Parse(await h.CreateCategoryAsync("Cat"));
        var first = Guid.Parse(await h.CreateTrashableRefRequiredAsync("R1", categoryId));
        var second = Guid.Parse(await h.CreateTrashableRefRequiredAsync("R2", categoryId));
        await h.Service.DeleteAsync(RequiredRef, first.ToString());
        await h.Service.DeleteAsync(RequiredRef, second.ToString());

        var ex = await Assert.ThrowsAsync<RelationConflictException>(
            () => h.Service.DeleteAsync("fxCategory", categoryId.ToString(), purge: true));

        ex.Message.Should().Contain("2 trashed").And.Contain(RequiredRef);
        (await h.Repository.GetByIdAsync("fxCategory", categoryId.ToString())).Should().NotBeNull();
        foreach (var id in new[] { first, second })
        {
            var row = (TrashableRestrictRefRequired)(await h.Repository.GetByIdAsync(
                RequiredRef, id.ToString(), DeletedFilter.With))!;
            row.CategoryId.Should().Be(categoryId);
            row.DeletedAt.Should().NotBeNull();
        }
    }

    [Fact]
    public async Task A_refusal_by_one_relation_leaves_the_trashed_rows_of_the_other_relation_untouched()
    {
        using var h = PurgeIntegrityHarness.Create();
        var (categoryId, nullableId) = await TrashedRefAsync(h);
        var requiredId = Guid.Parse(await h.CreateTrashableRefRequiredAsync("R", categoryId));
        await h.Service.DeleteAsync(RequiredRef, requiredId.ToString());

        await Assert.ThrowsAsync<RelationConflictException>(
            () => h.Service.DeleteAsync("fxCategory", categoryId.ToString(), purge: true));

        (await StoredRefAsync(h, nullableId)).CategoryId.Should().Be(categoryId);
    }

    [Fact]
    public async Task Live_referencing_row_still_blocks_the_purge_and_changes_nothing()
    {
        using var h = PurgeIntegrityHarness.Create();
        var categoryId = Guid.Parse(await h.CreateCategoryAsync("Cat"));
        var refId = Guid.Parse(await h.CreateTrashableRefAsync("Ref", categoryId));

        await Assert.ThrowsAsync<RelationConflictException>(
            () => h.Service.DeleteAsync("fxCategory", categoryId.ToString(), purge: true));

        (await h.Repository.GetByIdAsync("fxCategory", categoryId.ToString())).Should().NotBeNull();
        (await StoredRefAsync(h, refId)).CategoryId.Should().Be(categoryId);
    }

    [Fact]
    public async Task One_live_and_one_trashed_row_refuse_the_purge_and_the_trashed_key_is_kept()
    {
        using var h = PurgeIntegrityHarness.Create();
        var (categoryId, trashedId) = await TrashedRefAsync(h);
        await h.CreateTrashableRefAsync("Live", categoryId);

        await Assert.ThrowsAsync<RelationConflictException>(
            () => h.Service.DeleteAsync("fxCategory", categoryId.ToString(), purge: true));

        (await h.Repository.GetByIdAsync("fxCategory", categoryId.ToString())).Should().NotBeNull();
        (await StoredRefAsync(h, trashedId)).CategoryId.Should().Be(categoryId);
    }

    [Fact]
    public async Task Folder_purge_leaves_a_trashed_file_that_restores_into_the_library_root()
    {
        using var h = PurgeIntegrityHarness.Create();
        var folderId = Guid.Parse(await h.CreateFolderAsync("F"));
        var fileId = Guid.NewGuid();
        await h.Repository.CreateAsync("file", new File
        {
            Id = fileId, StorageKey = "k", FileName = "a.png", ContentType = "image/png", Size = 1,
            Status = "published", FolderId = folderId,
        });
        (await h.Service.DeleteAsync("file", fileId.ToString())).Should().BeTrue();

        var purged = await h.Service.DeleteAsync("mediafolder", folderId.ToString(), purge: true);

        purged.Should().BeTrue();
        (await h.Service.RestoreAsync("file", fileId.ToString())).Should().NotBeNull();
        var root = await h.Service.QueryAsync("file", new QueryModel(
            null, new ComparisonFilter("folderId", QueryOperator.Null, null), [], 0, 0, null));
        root.Data.Should().Contain(d => (Guid)d["id"]! == fileId);
    }
}
