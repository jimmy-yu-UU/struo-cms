// tests/Struo.Tests/Query/QueryEnvelopeShapeApiTests.cs
using System.Net;
using System.Text;
using System.Text.Json;
using AwesomeAssertions;
using Struo.Tests.Support;
using Xunit;

namespace Struo.Tests.Query;

/// <summary>
/// A <c>POST /query</c> body field of the wrong JSON kind is a 400 that names the field and what it takes,
/// never a 500. The marker 987654 stands for a value the answer must not repeat.
/// </summary>
[Collection("ApiIntegration")]
public sealed class QueryEnvelopeShapeApiTests(ApiFactory factory)
{
    private async Task<(HttpStatusCode Status, string Text)> PostAsync(string json, string collection = "language")
    {
        using var content = new StringContent(json, Encoding.UTF8, "application/json");
        var response = await factory.CreateClient().PostAsync($"/api/items/{collection}/query", content);
        return (response.StatusCode, await response.Content.ReadAsStringAsync());
    }

    [Theory]
    [InlineData("""{"search":987654}""", "search")]
    [InlineData("""{"search":true}""", "search")]
    [InlineData("""{"search":{"a":987654}}""", "search")]
    [InlineData("""{"search":[987654]}""", "search")]
    [InlineData("""{"sort":[987654]}""", "sort")]
    [InlineData("""{"sort":[true]}""", "sort")]
    [InlineData("""{"sort":[{"a":987654}]}""", "sort")]
    [InlineData("""{"sort":["code",987654]}""", "sort")]
    [InlineData("""{"fields":[987654]}""", "fields")]
    [InlineData("""{"fields":["code",{"a":987654}]}""", "fields")]
    [InlineData("""{"limit":"987654"}""", "limit")]
    [InlineData("""{"limit":true}""", "limit")]
    [InlineData("""{"limit":{"a":987654}}""", "limit")]
    [InlineData("""{"offset":"987654"}""", "offset")]
    [InlineData("""{"offset":[987654]}""", "offset")]
    [InlineData("""{"deep":{"x":{"limit":"987654"}}}""", "limit")]
    [InlineData("""{"deep":{"x":{"offset":true}}}""", "offset")]
    [InlineData("""{"deep":{"x":{"fields":[987654]}}}""", "fields")]
    [InlineData("""{"deep":{"x":{"sort":[987654]}}}""", "sort")]
    [InlineData("""{"facets":"code"}""", "facets")]
    [InlineData("""{"facets":[987654]}""", "facets")]
    [InlineData("""{"aggregate":"count"}""", "aggregate")]
    [InlineData("""{"aggregate":{"count":"code"}}""", "aggregate.count")]
    [InlineData("""[]""", "body")]
    [InlineData("""987654""", "body")]
    [InlineData("""null""", "body")]
    public async Task A_body_field_of_the_wrong_JSON_kind_is_a_400_that_names_it(string body, string field)
    {
        var (status, text) = await PostAsync(body);
        status.Should().Be(HttpStatusCode.BadRequest, text);
        text.Should().Contain("BAD_USER_INPUT").And.Contain($"'{field}'").And.NotContain("987654");
    }

    // A key that scopes or shapes the result must be of its kind: a filter that is not an object would
    // otherwise be dropped and the caller would read every row as filtered.
    [Theory]
    [InlineData("""{"filter":"status"}""", "filter")]
    [InlineData("""{"filter":[{"code":{"_eq":"en"}}]}""", "filter")]
    [InlineData("""{"filter":987654}""", "filter")]
    [InlineData("""{"filter":true}""", "filter")]
    [InlineData("""{"deep":"x"}""", "deep")]
    [InlineData("""{"deep":[]}""", "deep")]
    [InlineData("""{"deep":987654}""", "deep")]
    [InlineData("""{"deep":{"x":987654}}""", "deep.x")]
    [InlineData("""{"deep":{"x":"text"}}""", "deep.x")]
    [InlineData("""{"deep":{"x":[]}}""", "deep.x")]
    [InlineData("""{"deep":{"x":true}}""", "deep.x")]
    [InlineData("""{"sort":"code"}""", "sort")]
    [InlineData("""{"sort":{"a":987654}}""", "sort")]
    [InlineData("""{"sort":987654}""", "sort")]
    [InlineData("""{"fields":"code"}""", "fields")]
    [InlineData("""{"fields":{"a":987654}}""", "fields")]
    [InlineData("""{"deep":{"x":{"filter":"status"}}}""", "filter")]
    [InlineData("""{"deep":{"x":{"filter":[987654]}}}""", "filter")]
    [InlineData("""{"deep":{"x":{"sort":"code"}}}""", "sort")]
    [InlineData("""{"deep":{"x":{"fields":"code"}}}""", "fields")]
    [InlineData("""{"deep":{"x":{"deep":"y"}}}""", "deep")]
    [InlineData("""{"deep":{"x":{"deep":{"y":987654}}}}""", "deep.x.deep.y")]
    [InlineData("""{"deep":{"x":{"deep":{"y":{"filter":"s"}}}}}""", "filter")]
    public async Task A_key_that_shapes_the_result_must_be_of_its_kind(string body, string key)
    {
        var (status, text) = await PostAsync(body);
        status.Should().Be(HttpStatusCode.BadRequest, text);
        text.Should().Contain("BAD_USER_INPUT").And.Contain($"'{key}' must be").And.NotContain("987654");
    }

    [Fact]
    public async Task A_null_key_means_the_key_is_not_given()
    {
        var plain = await PostAsync("{}");
        var nulls = await PostAsync("""{"filter":null,"sort":null,"fields":null,"deep":null,"search":null}""");
        plain.Status.Should().Be(HttpStatusCode.OK, plain.Text);
        nulls.Status.Should().Be(HttpStatusCode.OK, nulls.Text);
        JsonDocument.Parse(nulls.Text).RootElement.GetProperty("data").GetArrayLength()
            .Should().Be(JsonDocument.Parse(plain.Text).RootElement.GetProperty("data").GetArrayLength());

        var deepNulls = await PostAsync(
            """{"deep":{"articles":null}}""", "fxCategory");
        deepNulls.Status.Should().Be(HttpStatusCode.OK, deepNulls.Text);
        var entryNulls = await PostAsync(
            """{"deep":{"articles":{"filter":null,"sort":null,"fields":null,"deep":null}}}""", "fxCategory");
        entryNulls.Status.Should().Be(HttpStatusCode.OK, entryNulls.Text);
    }

    [Fact]
    public async Task A_body_with_every_key_of_the_right_kind_is_answered()
    {
        var (status, text) = await PostAsync(
            """{"filter":{"code":{"_eq":"en"}},"sort":["code"],"fields":["code"],"limit":5,"offset":0,"search":"en","deep":{}}""");
        status.Should().Be(HttpStatusCode.OK, text);
        var (deepStatus, deepText) = await PostAsync(
            """{"deep":{"articles":{"filter":{"status":{"_eq":"draft"}},"sort":["status"],"fields":["status"],"limit":2,"offset":0,"deep":{}}}}""",
            "fxCategory");
        deepStatus.Should().Be(HttpStatusCode.OK, deepText);
    }
}
