using System.Net.Http.Json;
using System.Text.Json;
using AwesomeAssertions;
using Struo.Tests.Support;
using Xunit;

namespace Struo.Tests.GraphQl;

[Collection("ApiIntegration")]
public class FacetsGraphQlTests(ApiFactory factory)
{
    private readonly ApiFactory _factory = factory;

    private static async Task<JsonElement> Data(HttpClient client, string query)
    {
        var r = await client.PostAsJsonAsync("/graphql", new { query });
        var doc = JsonDocument.Parse(await r.Content.ReadAsStringAsync());
        doc.RootElement.TryGetProperty("errors", out var errors).Should().BeFalse(doc.RootElement.GetRawText());
        return doc.RootElement.GetProperty("data");
    }

    [Fact]
    public async Task List_field_accepts_facets_and_aggregate_and_returns_them_on_the_list_type()
    {
        var (client, cat, _) = await FacetSeed.SeedAsync(_factory);
        var data = await Data(client, $$"""
            { articles(filter: { categoryId: { eq: "{{cat}}" } }, facets: ["status", "tags.name"], aggregate: { count: ["publishedAt"], max: ["publishedAt"] }) {
                total
                facets { field values { value count } }
                aggregate
            } }
            """);
        var list = data.GetProperty("articles");
        list.GetProperty("total").GetInt32().Should().Be(3);
        var facets = list.GetProperty("facets").EnumerateArray().ToList();
        facets.Select(f => f.GetProperty("field").GetString()).Should().Equal("status", "tags.name");
        facets[0].GetProperty("values").EnumerateArray().Select(v => (v.GetProperty("value").GetString(), v.GetProperty("count").GetInt32()))
            .Should().Equal(("published", 2), ("draft", 1));
        facets[1].GetProperty("values")[0].GetProperty("count").GetInt32().Should().Be(2);
        list.GetProperty("aggregate").GetProperty("count").GetProperty("publishedAt").GetInt64().Should().Be(3);
        list.GetProperty("aggregate").GetProperty("max").GetProperty("publishedAt").GetString().Should().StartWith("2026-01-02");
    }

    [Fact]
    public async Task Without_the_arguments_facets_is_empty_and_aggregate_is_null()
    {
        var (client, cat, _) = await FacetSeed.SeedAsync(_factory);
        var data = await Data(client, $$"""{ articles(filter: { categoryId: { eq: "{{cat}}" } }) { total facets { field } aggregate } }""");
        data.GetProperty("articles").GetProperty("facets").GetArrayLength().Should().Be(0);
        data.GetProperty("articles").GetProperty("aggregate").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Fact]
    public async Task Invalid_facet_path_is_a_BAD_USER_INPUT_error()
    {
        var (client, _, _) = await FacetSeed.SeedAsync(_factory);
        var r = await client.PostAsJsonAsync("/graphql", new { query = """{ articles(facets: ["nope"]) { total } }""" });
        var doc = JsonDocument.Parse(await r.Content.ReadAsStringAsync());
        var err = doc.RootElement.GetProperty("errors")[0];
        err.GetProperty("message").GetString().Should().Be("Unknown field 'nope' on collection 'article'.");
        err.GetProperty("extensions").GetProperty("code").GetString().Should().Be("BAD_USER_INPUT");
    }

    [Fact]
    public async Task Nested_to_many_list_fields_do_not_expose_the_facets_argument()
    {
        var (client, cat, _) = await FacetSeed.SeedAsync(_factory);
        var r = await client.PostAsJsonAsync("/graphql", new { query = $$"""{ articles(filter: { categoryId: { eq: "{{cat}}" } }) { items { tags(facets: ["name"]) { id } } } }""" });
        var doc = JsonDocument.Parse(await r.Content.ReadAsStringAsync());
        doc.RootElement.TryGetProperty("errors", out var errors).Should().BeTrue();
        errors[0].GetProperty("message").GetString().Should().Contain("facets");
    }
}
