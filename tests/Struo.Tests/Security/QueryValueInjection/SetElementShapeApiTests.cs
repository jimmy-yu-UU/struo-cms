// tests/Struo.Tests/Security/QueryValueInjection/SetElementShapeApiTests.cs
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using SqlSugar;
using Struo.Infrastructure.Localization;
using Struo.Tests.Support;
using Xunit;

namespace Struo.Tests.Security.QueryValueInjection;

/// <summary>
/// A text set whose elements are numbers, booleans, null or nested lists, or whose value is a number or a
/// boolean, is a 400 on every entrance, never a 500, and the answer never repeats the value. A JSON object
/// (as an element or in place of a list) is read as its raw JSON text, and a string in place of a list is
/// one comma-separated list.
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
    [InlineData("""{"filter":{"code":{"_in":[null]}}}""")]
    [InlineData("""{"filter":{"code":{"_nin":[null]}}}""")]
    [InlineData("""{"filter":{"code":{"_in":["a",null]}}}""")]
    [InlineData("""{"filter":{"code":{"_in":["a",{"a":1},5]}}}""")]
    public async Task A_null_element_of_a_text_set_is_a_400(string body)
    {
        var (status, text) = await PostAsync(factory.CreateClient(), body);
        status.Should().Be(HttpStatusCode.BadRequest, text);
        text.Should().Contain("BAD_USER_INPUT").And.Contain("'code'");
    }

    // How a text field reads each JSON shape of a set. {C} is a code that exists; "match" is the rows with
    // that code, "none" no row, "all" every row and "rest" every row but the matching ones.
    [Theory]
    [InlineData("_in", """["{C}"]""", "match")]
    [InlineData("_in", "\"{C}\"", "match")]
    [InlineData("_in", "\"{C},zz-none\"", "match")]
    [InlineData("_in", """["{C}","zz-none"]""", "match")]
    [InlineData("_in", """[{"a":1}]""", "none")]
    [InlineData("_in", """["{C}",{"a":1}]""", "match")]
    [InlineData("_in", """{"a":1}""", "none")]
    [InlineData("_in", """{"a":1,"b":2}""", "none")]
    [InlineData("_in", """[]""", "none")]
    [InlineData("_in", "\"\"", "none")]
    [InlineData("_nin", """["{C}"]""", "rest")]
    [InlineData("_nin", "\"{C}\"", "rest")]
    [InlineData("_nin", """[{"a":1}]""", "all")]
    [InlineData("_nin", """{"a":1}""", "all")]
    [InlineData("_nin", """[]""", "all")]
    [InlineData("_nin", "\"\"", "all")]
    public async Task A_text_field_reads_a_string_as_a_comma_list_and_an_object_as_its_JSON_text(
        string op, string value, string expected)
    {
        var db = factory.Services.GetRequiredService<ISqlSugarClient>();
        var codes = (await db.Queryable<Language>().ToListAsync()).Select(l => l.Code).ToList();
        codes.Should().NotBeEmpty();
        var code = codes[0];

        var body = "{\"filter\":{\"code\":{\"" + op + "\":" + value.Replace("{C}", code) + "}}}";
        var (status, text) = await PostAsync(factory.CreateClient(), body);
        status.Should().Be(HttpStatusCode.OK, text);

        var matching = codes.Count(c => c == code);
        var expectedCount = expected switch
        {
            "match" => matching,
            "none" => 0,
            "all" => codes.Count,
            _ => codes.Count - matching,
        };
        JsonDocument.Parse(text).RootElement.GetProperty("data").GetArrayLength().Should().Be(expectedCount, text);
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
