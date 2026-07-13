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
            deep: null);

        q.Limit.Should().Be(5);
        q.Offset.Should().Be(10);
        q.Search.Should().Be("term");
        q.Filter.Should().NotBeNull();
        q.Deep.Should().BeNull();
    }

    [Fact]
    public void BuildQuery_passes_deep_through_unchanged()
    {
        var deep = new DeepSpec(new Dictionary<string, DeepRelationSpec>
        {
            ["category"] = new DeepRelationSpec(null, null,
                new DeepSpec(new Dictionary<string, DeepRelationSpec> { ["parent"] = new DeepRelationSpec(null, null) }))
        });

        var q = GraphQlQueryBuilder.BuildQuery(null, null, null, null, null, deep);

        q.Deep.Should().BeSameAs(deep);
        q.Deep!.Relations["category"].Deep!.Relations.Should().ContainKey("parent");
    }

    [Fact]
    public void BuildQuery_defaults_limit_and_offset_to_zero_when_absent()
    {
        // 0 = "let the validator clamp to DefaultLimit" (QueryValidator owns clamping).
        var q = GraphQlQueryBuilder.BuildQuery(null, null, null, null, null, deep: null);

        q.Limit.Should().Be(0);
        q.Offset.Should().Be(0);
    }

    [Fact]
    public void BuildQuery_flattens_nested_M2O_relation_filter_to_dotted_path()
    {
        Func<string, string, string?> rel = (_, key) => key == "category" ? "category" : null;

        var q = GraphQlQueryBuilder.BuildQuery(
            filter: new Dictionary<string, object?>
            {
                ["category"] = new Dictionary<string, object?>
                {
                    ["name"] = new Dictionary<string, object?> { ["eq"] = "Tech" }
                }
            },
            sort: null, limit: null, offset: null, search: null,
            deep: null,
            collection: "article", relationTarget: rel);

        var cmp = q.Filter.Should().BeOfType<ComparisonFilter>().Subject;
        cmp.FieldPath.Should().Be("category.name");
        cmp.Value.Should().Be("Tech");
    }
}
