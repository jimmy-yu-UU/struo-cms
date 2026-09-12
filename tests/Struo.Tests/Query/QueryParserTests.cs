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

    [Fact]
    public void Query_string_facets_is_a_comma_list_in_request_order()
    {
        var q = QueryParser.ParseQueryString(new Dictionary<string, string?> { ["facets"] = "status, categoryId,tags,category.name" });
        q.Facets.Should().Equal("status", "categoryId", "tags", "category.name");
    }

    // Finding C: unlike the aggregate field-list split (which stays RemoveEmptyEntries-tolerant, see
    // Query_string_aggregate_field_list_tolerates_a_trailing_comma below), the facets comma list must
    // KEEP a blank entry so QueryValidator's "Facet path must not be empty." rejects it with a 400 —
    // matching the manuals (docs/guide/{en,zh-TW}/10-query-basics.md) and DSL spec §6.
    [Fact]
    public void Query_string_facets_keeps_a_blank_entry_in_the_comma_list()
    {
        var q = QueryParser.ParseQueryString(new Dictionary<string, string?> { ["facets"] = "status,,tags" });
        q.Facets.Should().Equal("status", "", "tags");
    }

    [Fact]
    public void Query_string_facets_entirely_blank_means_not_requested()
    {
        QueryParser.ParseQueryString(new Dictionary<string, string?> { ["facets"] = "" }).Facets.Should().BeNull();
        QueryParser.ParseQueryString(new Dictionary<string, string?> { ["facets"] = "  " }).Facets.Should().BeNull();
    }

    // Pins that the aggregate field-list split is a SEPARATE split from the facets one (finding C):
    // it must stay tolerant of a trailing/empty entry, unlike facets above.
    [Fact]
    public void Query_string_aggregate_field_list_tolerates_a_trailing_comma()
    {
        var q = QueryParser.ParseQueryString(new Dictionary<string, string?> { ["aggregate[sum]"] = "price," });
        q.Aggregate!.Fields[AggregateOp.Sum].Should().Equal("price");
    }

    [Fact]
    public void Query_string_without_facets_or_aggregate_leaves_both_null()
    {
        var q = QueryParser.ParseQueryString(new Dictionary<string, string?> { ["limit"] = "5" });
        q.Facets.Should().BeNull();
        q.Aggregate.Should().BeNull();
    }

    [Fact]
    public void Query_string_aggregate_keys_map_to_ops_with_field_lists()
    {
        var q = QueryParser.ParseQueryString(new Dictionary<string, string?>
        {
            ["aggregate[sum]"] = "price", ["aggregate[max]"] = "price,rating", ["aggregate[count]"] = "publishedAt",
        });
        q.Aggregate!.Fields[AggregateOp.Sum].Should().Equal("price");
        q.Aggregate.Fields[AggregateOp.Max].Should().Equal("price", "rating");
        q.Aggregate.Fields[AggregateOp.Count].Should().Equal("publishedAt");
        q.Aggregate.Fields.Should().NotContainKey(AggregateOp.Min);
    }

    [Fact]
    public void Query_string_unknown_aggregate_op_throws()
    {
        var act = () => QueryParser.ParseQueryString(new Dictionary<string, string?> { ["aggregate[median]"] = "price" });
        act.Should().Throw<QueryException>().WithMessage("*Unknown aggregate op 'median'*");
    }

    [Fact]
    public void Query_string_malformed_aggregate_key_throws()
    {
        var act = () => QueryParser.ParseQueryString(new Dictionary<string, string?> { ["aggregate"] = "price" });
        act.Should().Throw<QueryException>().WithMessage("*Malformed aggregate key*");
    }

    // Finding I (pin existing behaviour): a typo'd key that merely STARTS WITH "aggregate" but is not
    // "aggregate[<op>]" — e.g. "aggregates" — deliberately still throws Malformed, naming the actual
    // key, rather than being silently ignored as an unrelated query-string parameter.
    [Fact]
    public void Query_string_aggregate_like_typo_key_throws_malformed_with_the_key_named()
    {
        var act = () => QueryParser.ParseQueryString(new Dictionary<string, string?> { ["aggregates"] = "1" });
        act.Should().Throw<QueryException>().WithMessage("*Malformed aggregate key 'aggregates'*");
    }

    [Fact]
    public void Envelope_facets_and_aggregate_are_equivalent_to_the_query_string()
    {
        var env = JsonDocument.Parse("""
            {"facets":["status","tags","category.name"],
             "aggregate":{"sum":["price"],"max":["price","rating"],"count":["publishedAt"]}}
            """).RootElement;
        var q = QueryParser.ParseEnvelope(env);
        q.Facets.Should().Equal("status", "tags", "category.name");
        q.Aggregate!.Fields[AggregateOp.Sum].Should().Equal("price");
        q.Aggregate.Fields[AggregateOp.Max].Should().Equal("price", "rating");
    }

    [Fact]
    public void Envelope_facets_must_be_an_array_of_strings()
    {
        var env = JsonDocument.Parse("""{"facets":"status"}""").RootElement;
        var act = () => QueryParser.ParseEnvelope(env);
        act.Should().Throw<QueryException>().WithMessage("*'facets' must be an array of strings*");
    }

    [Fact]
    public void Envelope_aggregate_values_must_be_arrays_of_strings()
    {
        var env = JsonDocument.Parse("""{"aggregate":{"sum":"price"}}""").RootElement;
        var act = () => QueryParser.ParseEnvelope(env);
        act.Should().Throw<QueryException>().WithMessage("*'aggregate.sum' must be an array of strings*");
    }

    [Fact]
    public void Envelope_unknown_aggregate_op_throws()
    {
        var env = JsonDocument.Parse("""{"aggregate":{"median":["price"]}}""").RootElement;
        var act = () => QueryParser.ParseEnvelope(env);
        act.Should().Throw<QueryException>().WithMessage("*Unknown aggregate op 'median'*");
    }
}
