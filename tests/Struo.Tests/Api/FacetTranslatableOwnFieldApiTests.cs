using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AwesomeAssertions;
using Struo.Tests.Support;
using Xunit;

namespace Struo.Tests.Api;

// A translatable own field lives on the translation sidecar, so a facet or an aggregate over it is
// a 400 on every entry point. The same field reached through a relation (articles.title) still facets.
[Collection("ApiIntegration")]
public class FacetTranslatableOwnFieldApiTests(ApiFactory factory)
{
    private const string FacetMessage = "Field 'title' on collection 'fxArticle' is translatable and cannot be used as a facet.";
    private const string AggregateMessage = "Field 'title' on collection 'fxArticle' is translatable and cannot be aggregated.";
    private const string UnknownHidden = "Unknown field 'internalSlug' on collection 'fxArticle'.";

    private static readonly string[] TitleField = ["title"];

    private static async Task<(HttpStatusCode Status, string Body)> Send(Task<HttpResponseMessage> call)
    {
        var r = await call;
        return (r.StatusCode, await r.Content.ReadAsStringAsync());
    }

    [Theory]
    [InlineData("title", FacetMessage)]
    [InlineData("internalSlug", UnknownHidden)]
    public async Task Facet_on_an_own_translatable_field_is_400_on_the_query_string(string field, string message)
    {
        var client = await factory.CreateAuthenticatedClientAsync();
        var (status, body) = await Send(client.GetAsync($"/api/items/fxArticle?facets={field}"));
        status.Should().Be(HttpStatusCode.BadRequest, body);
        body.Should().Contain("BAD_USER_INPUT").And.Contain(message);
    }

    [Fact]
    public async Task Facet_on_an_own_translatable_field_is_400_in_the_envelope()
    {
        var client = await factory.CreateAuthenticatedClientAsync();
        var (status, body) = await Send(client.PostAsJsonAsync("/api/items/fxArticle/query", new { facets = TitleField }));
        status.Should().Be(HttpStatusCode.BadRequest, body);
        body.Should().Contain(FacetMessage);
    }

    [Theory]
    [InlineData("count")]
    [InlineData("sum")]
    [InlineData("min")]
    [InlineData("max")]
    [InlineData("avg")]
    public async Task Aggregate_of_every_op_on_an_own_translatable_field_is_400_on_every_entry(string op)
    {
        var client = await factory.CreateAuthenticatedClientAsync();
        var (status, body) = await Send(client.GetAsync($"/api/items/fxArticle?aggregate[{op}]=title"));
        status.Should().Be(HttpStatusCode.BadRequest, body);
        body.Should().Contain("BAD_USER_INPUT").And.Contain(AggregateMessage);

        var envelope = new Dictionary<string, object> { ["aggregate"] = new Dictionary<string, string[]> { [op] = TitleField } };
        (status, body) = await Send(client.PostAsJsonAsync("/api/items/fxArticle/query", envelope));
        status.Should().Be(HttpStatusCode.BadRequest, body);
        body.Should().Contain(AggregateMessage);

        var gql = await GraphQlErrors(client, $$"""{ fxArticles(aggregate: { {{op}}: ["title"] }) { aggregate } }""");
        gql.Message.Should().Be(AggregateMessage);
        gql.Code.Should().Be("BAD_USER_INPUT");
    }

    [Fact]
    public async Task Aggregate_on_a_hidden_translatable_field_is_an_unknown_field()
    {
        var client = await factory.CreateAuthenticatedClientAsync();
        var (status, body) = await Send(client.GetAsync("/api/items/fxArticle?aggregate[count]=internalSlug"));
        status.Should().Be(HttpStatusCode.BadRequest, body);
        body.Should().Contain(UnknownHidden);
    }

    [Fact]
    public async Task Facet_on_an_own_translatable_field_is_a_BAD_USER_INPUT_error_in_GraphQl()
    {
        var client = await factory.CreateAuthenticatedClientAsync();
        var gql = await GraphQlErrors(client, """{ fxArticles(facets: ["title"]) { facets { field } } }""");
        gql.Message.Should().Be(FacetMessage);
        gql.Code.Should().Be("BAD_USER_INPUT");
    }

    [Fact]
    public async Task A_translatable_field_reached_through_a_relation_still_facets()
    {
        var (client, cat, _) = await FacetSeed.SeedAsync(factory);
        var r = await client.GetAsync($"/api/items/fxCategory?filter[id][_eq]={cat}&facets=articles.title");
        r.StatusCode.Should().Be(HttpStatusCode.OK, await r.Content.ReadAsStringAsync());
        var buckets = JsonDocument.Parse(await r.Content.ReadAsStringAsync()).RootElement
            .GetProperty("meta").GetProperty("facets").GetProperty("articles.title").EnumerateArray()
            .Select(b => b.GetProperty("value").GetString()).ToList();
        buckets.Should().BeEquivalentTo("Alpha", "Beta", "Gamma");
    }

    private static async Task<(string Message, string Code)> GraphQlErrors(HttpClient client, string query)
    {
        var r = await client.PostAsJsonAsync("/graphql", new { query });
        var error = JsonDocument.Parse(await r.Content.ReadAsStringAsync()).RootElement.GetProperty("errors")[0];
        return (error.GetProperty("message").GetString()!, error.GetProperty("extensions").GetProperty("code").GetString()!);
    }
}
