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
    [InlineData("""{"deep":{"x":{"limit":"987654"}}}""", "limit", "deep.x")]
    [InlineData("""{"deep":{"x":{"offset":true}}}""", "offset", "deep.x")]
    [InlineData("""{"deep":{"x":{"fields":[987654]}}}""", "fields", "deep.x")]
    [InlineData("""{"deep":{"x":{"sort":[987654]}}}""", "sort", "deep.x")]
    [InlineData("""{"facets":"code"}""", "facets")]
    [InlineData("""{"facets":[987654]}""", "facets")]
    [InlineData("""{"aggregate":"count"}""", "aggregate")]
    [InlineData("""{"aggregate":{"count":"code"}}""", "aggregate.count")]
    [InlineData("""[]""", "body")]
    [InlineData("""987654""", "body")]
    [InlineData("""null""", "body")]
    [InlineData("""{"deep":{"x":{"deep":{"y":{"limit":"987654"}}}}}""", "limit", "deep.x.deep.y")]
    [InlineData("""{"deep":{"x":{"deep":{"y":{"sort":[987654]}}}}}""", "sort", "deep.x.deep.y")]
    public async Task A_body_field_of_the_wrong_JSON_kind_is_a_400_that_names_it(string body, string field, string? path = null)
    {
        var (status, text) = await PostAsync(body);
        status.Should().Be(HttpStatusCode.BadRequest, text);
        text.Should().Contain("BAD_USER_INPUT").And.Contain($"'{field}'").And.NotContain("987654");
        if (path is not null) text.Should().Contain($"(in '{path}')");
    }

    // An element of _and or _or that is not an object, wherever the filter sits.
    [Theory]
    [InlineData("""{"filter":{"_and":["x"]}}""", "_and")]
    [InlineData("""{"filter":{"_and":[{"code":{"_eq":"en"}},null]}}""", "_and")]
    [InlineData("""{"filter":{"_or":[987654]}}""", "_or")]
    [InlineData("""{"filter":{"_or":[[{"code":{"_eq":"en"}}]]}}""", "_or")]
    [InlineData("""{"filter":{"_or":[{"_and":["x"]}]}}""", "_and")]
    [InlineData("""{"deep":{"x":{"filter":{"_and":["x"]}}}}""", "_and")]
    [InlineData("""{"filter":{"tags":{"_some":{"_or":[true]}}}}""", "_or")]
    public async Task An_element_of_and_or_or_that_is_not_an_object_is_a_400_that_names_it(string body, string group)
    {
        var (status, text) = await PostAsync(body);
        status.Should().Be(HttpStatusCode.BadRequest, text);
        text.Should().Contain("BAD_USER_INPUT").And.Contain($"'{group}' must be an array of objects").And.NotContain("987654");
    }

    [Theory]
    [InlineData("""{"filter":{"tags":"x"}}""")]
    [InlineData("""{"filter":{"tags":{"_some":"x"}}}""")]
    [InlineData("""{"filter":{"tags":{"_some":[1]}}}""")]
    [InlineData("""{"filter":{"code":["_eq"]}}""")]
    public async Task A_filter_value_that_is_not_an_object_is_a_400(string body)
    {
        var (status, text) = await PostAsync(body);
        status.Should().Be(HttpStatusCode.BadRequest, text);
        text.Should().Contain("BAD_USER_INPUT");
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
    [InlineData("""{"deep":{"x":{"filter":"status"}}}""", "filter", "deep.x")]
    [InlineData("""{"deep":{"x":{"filter":[987654]}}}""", "filter", "deep.x")]
    [InlineData("""{"deep":{"x":{"sort":"code"}}}""", "sort", "deep.x")]
    [InlineData("""{"deep":{"x":{"fields":"code"}}}""", "fields", "deep.x")]
    [InlineData("""{"deep":{"x":{"deep":"y"}}}""", "deep", "deep.x")]
    [InlineData("""{"deep":{"x":{"deep":{"y":987654}}}}""", "deep.x.deep.y")]
    [InlineData("""{"deep":{"x":{"deep":{"y":{"filter":"s"}}}}}""", "filter", "deep.x.deep.y")]
    public async Task A_key_that_shapes_the_result_must_be_of_its_kind(string body, string key, string? path = null)
    {
        var (status, text) = await PostAsync(body);
        status.Should().Be(HttpStatusCode.BadRequest, text);
        text.Should().Contain("BAD_USER_INPUT").And.Contain($"'{key}' must be").And.NotContain("987654");
        if (path is not null) text.Should().Contain($"(in '{path}')");
    }

    [Fact]
    public async Task A_null_body_key_means_the_key_is_not_given()
    {
        var plain = await PostAsync("{}");
        var nulls = await PostAsync(
            """{"filter":null,"sort":null,"fields":null,"deep":null,"search":null,"limit":null,"offset":null,"facets":null,"aggregate":null}""");
        plain.Status.Should().Be(HttpStatusCode.OK, plain.Text);
        nulls.Status.Should().Be(HttpStatusCode.OK, nulls.Text);
        JsonDocument.Parse(nulls.Text).RootElement.GetProperty("data").GetArrayLength()
            .Should().Be(JsonDocument.Parse(plain.Text).RootElement.GetProperty("data").GetArrayLength());

        var entryNulls = await PostAsync(
            """{"deep":{"articles":{"filter":null,"sort":null,"fields":null,"deep":null,"limit":null,"offset":null}}}""", "fxCategory");
        entryNulls.Status.Should().Be(HttpStatusCode.OK, entryNulls.Text);
    }

    // A relation entry that is null expands the relation with the defaults, the same as an empty object;
    // it is not "the relation is not given".
    [Fact]
    public async Task A_null_relation_entry_in_deep_expands_with_the_defaults_like_an_empty_object()
    {
        var nullEntry = await PostAsync("""{"deep":{"articles":null}}""", "fxCategory");
        var emptyEntry = await PostAsync("""{"deep":{"articles":{}}}""", "fxCategory");
        var noDeep = await PostAsync("{}", "fxCategory");
        nullEntry.Status.Should().Be(HttpStatusCode.OK, nullEntry.Text);
        emptyEntry.Status.Should().Be(HttpStatusCode.OK, emptyEntry.Text);
        JsonDocument.Parse(nullEntry.Text).RootElement.GetProperty("data").EnumerateArray()
            .All(row => row.TryGetProperty("articles", out _)).Should().BeTrue();
        JsonDocument.Parse(noDeep.Text).RootElement.GetProperty("data").EnumerateArray()
            .Any(row => row.TryGetProperty("articles", out var a) && a.ValueKind == JsonValueKind.Array).Should().BeFalse();
        JsonDocument.Parse(nullEntry.Text).RootElement.GetProperty("data").GetArrayLength()
            .Should().Be(JsonDocument.Parse(emptyEntry.Text).RootElement.GetProperty("data").GetArrayLength());
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
