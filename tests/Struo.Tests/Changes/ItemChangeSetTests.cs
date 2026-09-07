using AwesomeAssertions;
using Struo.Application.Changes;
using Xunit;

namespace Struo.Tests.Changes;

public class ItemChangeSetTests
{
    [Fact]
    public void Keeps_insertion_order_and_deduplicates_by_collection_and_id_case_insensitively()
    {
        var set = new ItemChangeSet();
        set.Add("article", "A1", ItemChangeKind.Updated);
        set.Add("category", "c1", ItemChangeKind.Updated);
        set.Add("Article", "a1", ItemChangeKind.Updated);
        set.Count.Should().Be(2);
        set.ToList().Select(c => (c.Collection, c.Id, c.Kind)).Should().Equal(
            ("article", "A1", ItemChangeKind.Updated), ("category", "c1", ItemChangeKind.Updated));
    }

    [Fact]
    public void Purged_wins_over_any_other_kind_for_the_same_item_but_never_the_reverse()
    {
        var set = new ItemChangeSet();
        set.Add("article", "a1", ItemChangeKind.Updated);
        set.Add("article", "a1", ItemChangeKind.Purged);
        set.Add("article", "a1", ItemChangeKind.Updated);
        set.ToList().Should().ContainSingle().Which.Kind.Should().Be(ItemChangeKind.Purged);
    }

    [Fact]
    public void ToList_returns_a_snapshot()
    {
        var set = new ItemChangeSet();
        set.Add("article", "a1", ItemChangeKind.Created);
        var first = set.ToList();
        set.Add("article", "a2", ItemChangeKind.Created);
        first.Should().HaveCount(1);
        set.ToList().Should().HaveCount(2);
    }
}
