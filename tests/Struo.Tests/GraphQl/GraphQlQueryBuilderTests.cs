// tests/Struo.Tests/GraphQl/GraphQlQueryBuilderTests.cs
using AwesomeAssertions;
using Struo.Api.GraphQl;
using Struo.Domain.Query;
using Xunit;

namespace Struo.Tests.GraphQl;

public class GraphQlQueryBuilderTests
{
    [Fact]
    public void ParseSort_handles_asc_and_desc_tokens()
    {
        var s = GraphQlQueryBuilder.ParseSort(new[] { "title", "-publishedAt" });

        s.Should().HaveCount(2);
        s[0].Field.Should().Be("title");
        s[0].Descending.Should().BeFalse();
        s[1].Field.Should().Be("publishedAt");
        s[1].Descending.Should().BeTrue();
    }

    [Fact]
    public void BuildQuery_maps_limit_offset_search_and_filter()
    {
        var q = GraphQlQueryBuilder.BuildQuery(
            filter: new Dictionary<string, object?> { ["status"] = new Dictionary<string, object?> { ["eq"] = "x" } },
            sort: new[] { "-id" }, limit: 5, offset: 10, search: "term",
            requestedRelations: Array.Empty<string>());

        q.Limit.Should().Be(5);
        q.Offset.Should().Be(10);
        q.Search.Should().Be("term");
        q.Filter.Should().NotBeNull();
        q.Deep.Should().BeNull();
    }

    [Fact]
    public void BuildQuery_sets_Deep_only_when_relations_requested()
    {
        var q = GraphQlQueryBuilder.BuildQuery(null, null, null, null, null, new[] { "category", "tags" });

        q.Deep.Should().NotBeNull();
        q.Deep!.Relations.Should().ContainKey("category");
        q.Deep.Relations.Should().ContainKey("tags");
    }

    [Fact]
    public void BuildQuery_defaults_limit_and_offset_to_zero_when_absent()
    {
        // 0 = "let the validator clamp to DefaultLimit" (QueryValidator owns clamping).
        var q = GraphQlQueryBuilder.BuildQuery(null, null, null, null, null, Array.Empty<string>());

        q.Limit.Should().Be(0);
        q.Offset.Should().Be(0);
    }
}
