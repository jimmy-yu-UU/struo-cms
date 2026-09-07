using AwesomeAssertions;
using Struo.Application.Search;
using Xunit;

namespace Struo.Tests.Search;

public class SearchOutcomeTests
{
    [Fact]
    public void NotHandled_has_no_ids()
    {
        SearchOutcome.NotHandled.Handled.Should().BeFalse();
        SearchOutcome.NotHandled.Ids.Should().BeNull();
    }

    [Fact]
    public void Candidates_are_handled_even_when_empty()
    {
        var empty = SearchOutcome.Candidates([]);
        empty.Handled.Should().BeTrue();
        empty.Ids.Should().BeEmpty();
        SearchOutcome.Candidates(["a", "b"]).Ids.Should().Equal("a", "b");
    }

    [Fact]
    public void Candidates_rejects_null()
    {
        var act = () => SearchOutcome.Candidates(null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public async Task NullSearchProvider_always_declines()
    {
        var outcome = await NullSearchProvider.Instance.SearchAsync(new SearchRequest("article", "x", "en", ["title"]));
        outcome.Handled.Should().BeFalse();
    }
}
