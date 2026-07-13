using System.Text.Json;
using AwesomeAssertions;
using Struo.Application.Query;
using Struo.Domain.Query;
using Xunit;

namespace Struo.Tests.Query;

public class NestedListArgsParseTests
{
    private static QueryModel Parse(string json) =>
        QueryParser.ParseEnvelope(JsonDocument.Parse(json).RootElement);

    [Fact]
    public void Envelope_parses_nested_filter_sort_limit_offset()
    {
        var model = Parse("""
        { "deep": { "tags": {
            "filter": { "name": { "_contains": "AI" } },
            "sort": ["name", "-createdAt"],
            "limit": 5,
            "offset": 2
        } } }
        """);

        var spec = model.Deep!.Relations["tags"];
        spec.Filter.Should().BeOfType<ComparisonFilter>()
            .Which.FieldPath.Should().Be("name");
        spec.Sort!.Select(s => (s.Field, s.Descending))
            .Should().Equal(("name", false), ("createdAt", true));
        spec.Limit.Should().Be(5);
        spec.Offset.Should().Be(2);
    }

    [Fact]
    public void Envelope_parses_args_at_nested_levels()
    {
        var model = Parse("""
        { "deep": { "category": { "deep": { "articles": { "limit": 3 } } } } }
        """);

        var articles = model.Deep!.Relations["category"].Deep!.Relations["articles"];
        articles.Limit.Should().Be(3);
    }

    [Fact]
    public void Envelope_without_args_leaves_them_null()
    {
        var model = Parse("""{ "deep": { "tags": {} } }""");
        var spec = model.Deep!.Relations["tags"];
        spec.Filter.Should().BeNull();
        spec.Sort.Should().BeNull();
        spec.Limit.Should().BeNull();
        spec.Offset.Should().BeNull();
    }
}
