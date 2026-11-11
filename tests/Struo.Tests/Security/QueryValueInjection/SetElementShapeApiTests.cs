// tests/Struo.Tests/Security/QueryValueInjection/SetElementShapeApiTests.cs
using System.Net;
using System.Net.Http.Json;
using System.Text;
using AwesomeAssertions;
using Struo.Tests.Support;
using Xunit;

namespace Struo.Tests.Security.QueryValueInjection;

/// <summary>
/// A set whose elements are not plain text (a number, a boolean, an object, a nested list) or whose
/// value is not a list is a 400 on every entrance, never a 500, and the answer never repeats the value.
/// </summary>
[Collection("ApiIntegration")]
public sealed class SetElementShapeApiTests(ApiFactory factory)
{
    private const string Marker = "987654";

    private static async Task<(HttpStatusCode Status, string Text)> PostAsync(HttpClient c, string json)
    {
        using var content = new StringContent(json, Encoding.UTF8, "application/json");
        var response = await c.PostAsync("/api/items/language/query", content);
        return (response.StatusCode, await response.Content.ReadAsStringAsync());
    }

    [Theory]
    [InlineData("""{"filter":{"code":{"_in":[987654]}}}""")]
    [InlineData("""{"filter":{"code":{"_nin":[987654]}}}""")]
    [InlineData("""{"filter":{"code":{"_in":["a",987654]}}}""")]
    [InlineData("""{"filter":{"code":{"_in":[true]}}}""")]
    [InlineData("""{"filter":{"code":{"_in":987654}}}""")]
    [InlineData("""{"filter":{"code":{"_nin":true}}}""")]
    [InlineData("""{"filter":{"code":{"_in":[[987654]]}}}""")]
    [InlineData("""{"filter":{"_or":[{"code":{"_in":["a",987654]}},{"sort":{"_eq":1}}]}}""")]
    [InlineData("""{"filter":{"_or":[{"code":{"_in":[987654]}}]}}""")]
    public async Task A_text_set_that_is_not_a_list_of_text_is_a_400(string body)
    {
        var (status, text) = await PostAsync(factory.CreateClient(), body);
        status.Should().Be(HttpStatusCode.BadRequest, text);
        text.Should().Contain("BAD_USER_INPUT").And.Contain("'code'").And.NotContain(Marker);
    }

    [Theory]
    [InlineData("""{"filter":{"sort":{"_in":[true]}}}""")]
    [InlineData("""{"filter":{"sort":{"_in":[{"a":987654}]}}}""")]
    [InlineData("""{"filter":{"sort":{"_nin":[[987654]]}}}""")]
    [InlineData("""{"filter":{"sort":{"_in":{"a":987654}}}}""")]
    [InlineData("""{"filter":{"sort":{"_eq":true}}}""")]
    [InlineData("""{"filter":{"sort":{"_eq":{"a":987654}}}}""")]
    [InlineData("""{"filter":{"sort":{"_eq":[[987654]]}}}""")]
    [InlineData("""{"filter":{"id":{"_in":[true]}}}""")]
    [InlineData("""{"filter":{"id":{"_eq":true}}}""")]
    [InlineData("""{"filter":{"id":{"_in":[{"a":987654}]}}}""")]
    [InlineData("""{"filter":{"enabled":{"_in":[{"a":987654}]}}}""")]
    [InlineData("""{"filter":{"enabled":{"_eq":{"a":987654}}}}""")]
    [InlineData("""{"filter":{"enabled":{"_in":[[true]]}}}""")]
    public async Task A_typed_field_with_a_boolean_object_or_nested_list_is_a_400(string body)
    {
        var (status, text) = await PostAsync(factory.CreateClient(), body);
        status.Should().Be(HttpStatusCode.BadRequest, text);
        text.Should().Contain("BAD_USER_INPUT").And.NotContain(Marker);
    }

    [Theory]
    [InlineData("filter[code][_in]=987654", HttpStatusCode.OK)]
    [InlineData("filter[code][_in]=a,,b", HttpStatusCode.OK)]
    [InlineData("filter[sort][_in]=true", HttpStatusCode.BadRequest)]
    [InlineData("filter[id][_in]=true", HttpStatusCode.BadRequest)]
    [InlineData("filter[sort][_in]=1,,2", HttpStatusCode.BadRequest)]
    [InlineData("filter[sort][_in]=,", HttpStatusCode.BadRequest)]
    [InlineData("filter[enabled][_in]=%7B%22a%22%3A1%7D", HttpStatusCode.BadRequest)]
    public async Task The_query_string_reads_every_element_as_text(string query, HttpStatusCode expected)
    {
        var response = await factory.CreateClient().GetAsync("/api/items/language?" + query);
        response.StatusCode.Should().Be(expected, await response.Content.ReadAsStringAsync());
    }

    [Theory]
    [InlineData("""{ languages(filter: { code: { in: [987654] } }) { items { id } } }""")]
    [InlineData("""{ languages(filter: { code: { nin: [true] } }) { items { id } } }""")]
    [InlineData("""{ languages(filter: { code: { in: 987654 } }) { items { id } } }""")]
    [InlineData("""{ languages(filter: { code: { in: [["a"]] } }) { items { id } } }""")]
    [InlineData("""{ languages(filter: { sort: { in: [true] } }) { items { id } } }""")]
    [InlineData("""{ languages(filter: { sort: { eq: true } }) { items { id } } }""")]
    [InlineData("""{ languages(filter: { sort: { in: [{ a: 987654 }] } }) { items { id } } }""")]
    public async Task Graphql_types_the_set_and_answers_a_validation_error(string query)
    {
        var response = await factory.CreateClient().PostAsJsonAsync("/graphql", new { query });
        var text = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().NotBe(HttpStatusCode.InternalServerError, text);
        text.Should().Contain("\"errors\"");
        text.Should().NotContainEquivalentOf("Unexpected Execution Error").And.NotContain("InvalidCast").And.NotContain(Marker);
    }
}
