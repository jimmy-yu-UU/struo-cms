// tests/Struo.Tests/Query/QueryEnvelopeShapeApiTests.cs
using System.Net;
using System.Text;
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
    private async Task<(HttpStatusCode Status, string Text)> PostAsync(string json)
    {
        using var content = new StringContent(json, Encoding.UTF8, "application/json");
        var response = await factory.CreateClient().PostAsync("/api/items/language/query", content);
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
}
