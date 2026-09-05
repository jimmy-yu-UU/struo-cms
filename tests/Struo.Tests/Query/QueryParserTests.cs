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

    [Fact]
    public void Envelope_multiple_top_level_fields_become_and()
    {
        var json = JsonDocument.Parse("""
        { "filter": { "status": { "_eq": "published" }, "title": { "_contains": "x" } } }
        """).RootElement;

        var q = QueryParser.ParseEnvelope(json);
        var logical = q.Filter.Should().BeOfType<LogicalFilter>().Subject;
        logical.Op.Should().Be(LogicalOperator.And);
        logical.Children.Should().HaveCount(2);
        logical.Children.Should().AllBeOfType<ComparisonFilter>();
        logical.Children.Cast<ComparisonFilter>().Select(c => c.FieldPath)
            .Should().BeEquivalentTo(["status", "title"]);
    }

    [Fact]
    public void Envelope_multiple_operators_on_one_field_become_and()
    {
        var json = JsonDocument.Parse("""
        { "filter": { "createdAt": { "_gt": 1, "_lt": 10 } } }
        """).RootElement;

        var q = QueryParser.ParseEnvelope(json);
        var logical = q.Filter.Should().BeOfType<LogicalFilter>().Subject;
        logical.Op.Should().Be(LogicalOperator.And);
        logical.Children.Should().HaveCount(2);
        logical.Children.Should().AllBeOfType<ComparisonFilter>();
        var cmps = logical.Children.Cast<ComparisonFilter>().ToList();
        cmps.Should().AllSatisfy(c => c.FieldPath.Should().Be("createdAt"));
        cmps.Select(c => c.Op).Should().BeEquivalentTo([QueryOperator.Gt, QueryOperator.Lt]);
    }

    [Fact]
    public void Envelope_parses_or_group()
    {
        var json = JsonDocument.Parse("""
        { "filter": { "_or": [ { "status": { "_eq": "a" } }, { "status": { "_eq": "b" } } ] } }
        """).RootElement;

        var q = QueryParser.ParseEnvelope(json);
        var logical = q.Filter.Should().BeOfType<LogicalFilter>().Subject;
        logical.Op.Should().Be(LogicalOperator.Or);
        logical.Children.Should().HaveCount(2);
        logical.Children.Should().AllBeOfType<ComparisonFilter>();
    }

    [Fact]
    public void ParseEnvelope_parses_nested_deep()
    {
        var env = JsonSerializer.SerializeToElement(new
        {
            deep = new { category = new { deep = new { parent = new { } } } }
        });

        var model = QueryParser.ParseEnvelope(env);

        model.Deep.Should().NotBeNull();
        var category = model.Deep!.Relations["category"];
        category.Deep.Should().NotBeNull();
        category.Deep!.Relations.Should().ContainKey("parent");
        category.Deep.Relations["parent"].Deep.Should().BeNull(); // leaf
    }

    [Fact]
    public void ParseEnvelope_keeps_flat_deep_backward_compatible()
    {
        var env = JsonSerializer.SerializeToElement(new
        {
            deep = new { category = new { fields = new[] { "name" } } }
        });

        var model = QueryParser.ParseEnvelope(env);

        var category = model.Deep!.Relations["category"];
        category.Fields.Should().ContainSingle().Which.Should().Be("name");
        category.Deep.Should().BeNull();
    }

    [Fact]
    public void Envelope_parses_some_predicate()
    {
        var json = JsonDocument.Parse("""
        { "filter": { "properties": { "_some": { "code": { "_eq": "vds-v" }, "valueNum": { "_gte": 60 } } } } }
        """).RootElement;
        var p = QueryParser.ParseEnvelope(json).Filter.Should().BeOfType<RelationPredicateFilter>().Subject;
        p.RelationPath.Should().Be("properties");
        p.Quantifier.Should().Be(RelationQuantifier.Some);
        p.Inner.Should().BeOfType<LogicalFilter>().Which.Children.Should().HaveCount(2);
    }

    [Fact]
    public void Envelope_some_and_none_on_one_key_become_two_predicates()
    {
        var json = JsonDocument.Parse("""
        { "filter": { "tags": { "_some": { "name": { "_eq": "a" } }, "_none": { "name": { "_eq": "b" } } } } }
        """).RootElement;
        var l = QueryParser.ParseEnvelope(json).Filter.Should().BeOfType<LogicalFilter>().Subject;
        l.Children.Should().AllBeOfType<RelationPredicateFilter>().And.HaveCount(2);
    }

    [Theory]
    [InlineData("""{ "filter": { "tags": { "_some": "x" } } }""")]
    [InlineData("""{ "filter": { "tags": { "_some": {} } } }""")]
    [InlineData("""{ "filter": { "tags": { "_some": { "name": { "_eq": "a" } }, "_eq": "b" } } }""")]
    public void Envelope_malformed_quantifiers_throw(string body)
    {
        var json = JsonDocument.Parse(body).RootElement;
        var act = () => QueryParser.ParseEnvelope(json);
        act.Should().Throw<QueryException>();
    }

    [Fact]
    public void QueryString_folds_same_prefix_some_conditions()
    {
        var q = QueryParser.ParseQueryString(new Dictionary<string, string?>
        {
            ["filter[properties._some.code][_eq]"] = "vds-v",
            ["filter[properties._some.valueNum][_gte]"] = "60",
        });
        var p = q.Filter.Should().BeOfType<RelationPredicateFilter>().Subject;
        p.RelationPath.Should().Be("properties");
        p.Inner.Should().BeOfType<LogicalFilter>().Which.Children.Should().HaveCount(2);
    }

    [Fact]
    public void QueryString_and_envelope_produce_equal_trees()
    {
        var qs = QueryParser.ParseQueryString(new Dictionary<string, string?> { ["filter[tags._none.name][_eq]"] = "internal" }).Filter;
        var env = QueryParser.ParseEnvelope(JsonDocument.Parse("""{ "filter": { "tags": { "_none": { "name": { "_eq": "internal" } } } } }""").RootElement).Filter;
        qs.Should().BeEquivalentTo(env, o => o.PreferringRuntimeMemberTypes());
    }
}
