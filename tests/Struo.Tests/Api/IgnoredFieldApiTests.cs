using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AwesomeAssertions;
using Struo.Tests.Support;
using Xunit;

namespace Struo.Tests.Api;

/// <summary>A <c>[CmsField]</c> on a <c>[SugarColumn(IsIgnore = true)]</c> property has no column: REST and GraphQL select it and refuse to query by it.</summary>
[Collection("ApiIntegration")]
public class IgnoredFieldApiTests(ApiFactory factory)
{
    private readonly ApiFactory _factory = factory;

    private async Task<(HttpClient Client, string CategoryId)> SeedAsync()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();
        var created = await client.PostAsJsonAsync("/api/items/fxCategory", new { name = "Ignored-" + Guid.NewGuid().ToString("N")[..8] });
        using var doc = JsonDocument.Parse(await created.Content.ReadAsStringAsync());
        return (client, doc.RootElement.GetProperty("data").GetProperty("id").GetString()!);
    }

    [Theory]
    [InlineData("filter[badge][_eq]=fx-badge", "Unknown field 'badge' on collection 'fxCategory'.")]
    [InlineData("sort=badge", "Unknown field 'badge' on collection 'fxCategory'.")]
    [InlineData("sort=-badge", "Unknown field 'badge' on collection 'fxCategory'.")]
    [InlineData("facets=badge", "Unknown field 'badge' on collection 'fxCategory'.")]
    [InlineData("aggregate[count]=badge", "Unknown field 'badge' on collection 'fxCategory'.")]
    [InlineData("facets=parent.badge", "Unknown field 'badge' on collection 'fxCategory'.")]
    public async Task Querying_by_a_field_without_a_column_is_400_with_the_unknown_field_message(string query, string message)
    {
        var (client, _) = await SeedAsync();

        var response = await client.GetAsync("/api/items/fxCategory?" + query);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).Should().Contain(message);
    }

    [Theory]
    [InlineData("filter[category.badge][_eq]=x")]
    [InlineData("sort=category.badge")]
    public async Task A_relation_path_ending_in_a_field_without_a_column_is_400(string query)
    {
        var (client, _) = await SeedAsync();

        var response = await client.GetAsync("/api/items/fxArticle?" + query);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).Should().Contain("Unknown field 'badge' on collection 'fxCategory' in path 'category.badge'.");
    }

    [Fact]
    public async Task Selecting_a_field_without_a_column_returns_its_value()
    {
        var (client, id) = await SeedAsync();

        var response = await client.GetAsync($"/api/items/fxCategory?filter[id][_eq]={id}&fields=name,badge");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        doc.RootElement.GetProperty("data")[0].GetProperty("badge").GetString().Should().Be("fx-badge");
    }

    [Fact]
    public async Task A_write_to_a_field_without_a_column_is_accepted_and_the_value_is_not_stored()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();

        var created = await client.PostAsJsonAsync(
            "/api/items/fxCategory", new { name = "Ignored-" + Guid.NewGuid().ToString("N")[..8], badge = "written" });

        var createdBody = await created.Content.ReadAsStringAsync();
        created.StatusCode.Should().Be(HttpStatusCode.Created, createdBody);
        using var createdDoc = JsonDocument.Parse(createdBody);
        var id = createdDoc.RootElement.GetProperty("data").GetProperty("id").GetString();
        createdDoc.RootElement.GetProperty("data").GetProperty("badge").GetString().Should().Be("written");

        var read = await client.GetAsync($"/api/items/fxCategory?filter[id][_eq]={id}&fields=badge");
        using var doc = JsonDocument.Parse(await read.Content.ReadAsStringAsync());
        doc.RootElement.GetProperty("data")[0].GetProperty("badge").GetString().Should().Be("fx-badge");
    }

    [Fact]
    public async Task The_schema_lists_a_field_without_a_column_as_ignored()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();

        var response = await client.GetAsync("/api/schema/fxCategory");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var fields = doc.RootElement.GetProperty("data").GetProperty("fields").EnumerateArray().ToList();
        fields.Single(f => f.GetProperty("name").GetString() == "badge").GetProperty("ignored").GetBoolean().Should().BeTrue();
        fields.Single(f => f.GetProperty("name").GetString() == "name").GetProperty("ignored").GetBoolean().Should().BeFalse();
    }

    private static async Task<(HttpStatusCode Status, JsonElement Root)> GraphQl(HttpClient client, string query)
    {
        var response = await client.PostAsJsonAsync("/graphql", new { query });
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return (response.StatusCode, doc.RootElement.Clone());
    }

    [Fact]
    public async Task The_graphql_filter_input_leaves_out_a_field_without_a_column_and_the_field_is_selectable()
    {
        var (client, id) = await SeedAsync();

        var (_, types) = await GraphQl(client,
            "{ __type(name: \"FxCategoryFilterInput\") { inputFields { name } } }");
        var names = types.GetProperty("data").GetProperty("__type").GetProperty("inputFields")
            .EnumerateArray().Select(f => f.GetProperty("name").GetString()).ToList();
        names.Should().Contain("name").And.NotContain("badge");

        var (_, selected) = await GraphQl(client,
            $"{{ fxCategories(filter: {{ id: {{ eq: \"{id}\" }} }}) {{ items {{ badge }} }} }}");
        selected.GetProperty("data").GetProperty("fxCategories").GetProperty("items")[0]
            .GetProperty("badge").GetString().Should().Be("fx-badge");
    }

    [Fact]
    public async Task A_graphql_filter_on_a_field_without_a_column_is_a_request_error_before_any_data()
    {
        var (client, _) = await SeedAsync();

        var (status, root) = await GraphQl(client, "{ fxCategories(filter: { badge: { eq: \"x\" } }) { items { id } } }");

        root.TryGetProperty("errors", out var errors).Should().BeTrue(root.ToString());
        errors.GetArrayLength().Should().BeGreaterThan(0);
        ((int)status).Should().Be(400, root.ToString());
        root.TryGetProperty("data", out _).Should().BeFalse(root.ToString());
        errors[0].GetProperty("message").GetString().Should().Be("The specified input object field `badge` does not exist.");
    }
}
