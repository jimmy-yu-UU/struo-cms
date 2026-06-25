// tests/Struo.Tests/Query/QueryParserTests.cs
using System.Text.Json;
using AwesomeAssertions;
using Struo.Application.Query;
using Struo.Domain.Query;
using Xunit;

namespace Struo.Tests.Query;

public class QueryParserTests
{
    [Fact]
    public void Envelope_parses_filter_sort_fields_paging()
    {
        var json = JsonDocument.Parse("""
        {
          "fields": ["id","title"],
          "filter": { "status": { "_eq": "published" } },
          "sort": ["-createdAt","title"],
          "limit": 10, "offset": 5, "search": "hello"
        }
        """).RootElement;

        var q = QueryParser.ParseEnvelope(json);

        q.Fields.Should().BeEquivalentTo(["id", "title"]);
        q.Limit.Should().Be(10);
        q.Offset.Should().Be(5);
        q.Search.Should().Be("hello");
        q.Sort.Should().ContainInOrder(new SortField("createdAt", true), new SortField("title", false));
        var cmp = q.Filter.Should().BeOfType<ComparisonFilter>().Subject;
        cmp.FieldPath.Should().Be("status");
        cmp.Op.Should().Be(QueryOperator.Eq);
    }

    [Fact]
    public void Envelope_parses_and_or_logical_groups()
    {
        var json = JsonDocument.Parse("""
        { "filter": { "_and": [ { "status": { "_eq": "published" } }, { "title": { "_contains": "x" } } ] } }
        """).RootElement;

        var q = QueryParser.ParseEnvelope(json);
        var logical = q.Filter.Should().BeOfType<LogicalFilter>().Subject;
        logical.Op.Should().Be(LogicalOperator.And);
        logical.Children.Should().HaveCount(2);
    }

    [Fact]
    public void QueryString_parses_bracket_filter_and_sort()
    {
        var qs = new Dictionary<string, string?>
        {
            ["filter[status][_eq]"] = "published",
            ["sort"] = "-createdAt,title",
            ["fields"] = "id,title",
            ["limit"] = "10", ["offset"] = "5", ["search"] = "hello"
        };

        var q = QueryParser.ParseQueryString(qs);

        q.Fields.Should().BeEquivalentTo(["id", "title"]);
        q.Sort.Should().ContainInOrder(new SortField("createdAt", true), new SortField("title", false));
        var cmp = q.Filter.Should().BeOfType<ComparisonFilter>().Subject;
        cmp.FieldPath.Should().Be("status");
        cmp.Op.Should().Be(QueryOperator.Eq);
        cmp.Value.Should().Be("published");
    }

    [Fact]
    public void Unknown_operator_throws()
    {
        var json = JsonDocument.Parse("""{ "filter": { "status": { "_bogus": "x" } } }""").RootElement;
        var act = () => QueryParser.ParseEnvelope(json);
        act.Should().Throw<QueryException>();
    }
}
