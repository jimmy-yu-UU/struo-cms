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

    private const string BodyKeyList = "fields, filter, sort, limit, offset, search, deep, facets, aggregate";
    private const string DeepKeyList = "fields, filter, sort, limit, offset, deep";

    // A misspelled key must not be ignored: the request would answer every row.
    [Theory]
    [InlineData("""{"fitler":{"code":{"_eq":"en"}}}""", "fitler", null, BodyKeyList)]
    [InlineData("""{"Filter":{"code":{"_eq":"en"}}}""", "Filter", null, BodyKeyList)]
    [InlineData("""{"filter":{"code":{"_eq":"en"}},"limt":5}""", "limt", null, BodyKeyList)]
    [InlineData("""{"unknown":null}""", "unknown", null, BodyKeyList)]
    [InlineData("""{"where":{}}""", "where", null, BodyKeyList)]
    [InlineData("""{"deep":{"x":{"fitler":{}}}}""", "fitler", "deep.x", DeepKeyList)]
    [InlineData("""{"deep":{"x":{"search":"a"}}}""", "search", "deep.x", DeepKeyList)]
    [InlineData("""{"deep":{"x":{"facets":[]}}}""", "facets", "deep.x", DeepKeyList)]
    [InlineData("""{"deep":{"x":{"unknown":null}}}""", "unknown", "deep.x", DeepKeyList)]
    [InlineData("""{"deep":{"x":{"limit":1,"Limit":2}}}""", "Limit", "deep.x", DeepKeyList)]
    [InlineData("""{"deep":{"x":{"deep":{"y":{"serch":"a"}}}}}""", "serch", "deep.x.deep.y", DeepKeyList)]
    [InlineData("""{"deep":{"x":{"deep":{"y":{"unknown":null}}}}}""", "unknown", "deep.x.deep.y", DeepKeyList)]
    public async Task A_key_the_parser_does_not_read_is_a_400_that_names_it_and_lists_the_accepted_keys(
        string body, string key, string? path, string accepted)
    {
        var (status, text) = await PostAsync(body);
        status.Should().Be(HttpStatusCode.BadRequest, text);
        text.Should().Contain("BAD_USER_INPUT").And.Contain($"Unknown key '{key}'").And.Contain($"the accepted keys are {accepted}.");
        if (path is not null) text.Should().Contain($"(in '{path}')");
        else text.Should().NotContain("(in 'deep");
    }

    private static string MessageOf(string text) =>
        JsonDocument.Parse(text).RootElement.GetProperty("error").GetProperty("message").GetString()!;

    [Fact]
    public async Task A_long_unknown_key_is_cut_in_the_message()
    {
        var key = new string('k', 300);
        var (status, text) = await PostAsync("{\"" + key + "\":1}");
        status.Should().Be(HttpStatusCode.BadRequest, text);
        var message = MessageOf(text);
        message.Should().Contain(new string('k', 64) + "\u2026").And.NotContain(new string('k', 65));
    }

    // The cut falls on a character boundary: a pair of surrogates is never split into U+FFFD.
    [Fact]
    public async Task The_cut_of_a_long_unknown_key_keeps_a_surrogate_pair_whole()
    {
        var key = new string('a', 63) + "\\uD83D\\uDE00" + new string('b', 20);
        var (status, text) = await PostAsync("{\"" + key + "\":1}");
        status.Should().Be(HttpStatusCode.BadRequest, text);
        var message = MessageOf(text);
        message.Should().Contain(new string('a', 63) + "\u2026").And.NotContain("\uFFFD");
        message.Any(char.IsSurrogate).Should().BeFalse("the cut keeps a surrogate pair whole or drops it");
    }

    // The last value of a repeated known key wins; a repeated or empty unknown key is unknown.
    [Fact]
    public async Task A_repeated_known_key_takes_the_last_value()
    {
        var (status, text) = await PostAsync("""{"limit":1,"limit":2}""");
        status.Should().Be(HttpStatusCode.OK, text);
        JsonDocument.Parse(text).RootElement.GetProperty("meta").GetProperty("limit").GetInt32().Should().Be(2);
    }

    [Theory]
    [InlineData("""{"zzz":1,"zzz":2}""", "zzz")]
    [InlineData("""{"":1}""", "")]
    public async Task A_repeated_or_empty_unknown_key_is_a_400(string body, string key)
    {
        var (status, text) = await PostAsync(body);
        status.Should().Be(HttpStatusCode.BadRequest, text);
        text.Should().Contain($"Unknown key '{key}'");
    }

    // Text that cannot be read as text (an escaped lone surrogate) is refused up front, in a name or in a
    // value, wherever it sits, and the answer does not repeat it. The JSON holds the backslash-u escape
    // as text, so this source holds no lone surrogate.
    private const string Lone = @"\ud800";
    private const string LoneLow = @"\udc00";

    [Theory]
    [InlineData("{\"" + Lone + "\":1}")]
    [InlineData("{\"" + LoneLow + "\":1}")]
    [InlineData("{\"filter\":{\"" + Lone + "\":{\"_eq\":\"a\"}}}")]
    [InlineData("{\"filter\":{\"code\":{\"" + Lone + "\":\"a\"}}}")]
    [InlineData("{\"filter\":{\"code\":{\"_eq\":\"" + Lone + "\"}}}")]
    [InlineData("{\"filter\":{\"code\":{\"_in\":[\"" + Lone + "\"]}}}")]
    [InlineData("{\"filter\":{\"_and\":[{\"code\":{\"_eq\":\"" + LoneLow + "\"}}]}}")]
    [InlineData("{\"deep\":{\"" + Lone + "\":{}}}")]
    [InlineData("{\"deep\":{\"x\":{\"" + Lone + "\":1}}}")]
    [InlineData("{\"deep\":{\"x\":{\"deep\":{\"" + Lone + "\":{}}}}}")]
    [InlineData("{\"deep\":{\"x\":{\"filter\":{\"code\":{\"_eq\":\"" + Lone + "\"}}}}}")]
    [InlineData("{\"aggregate\":{\"" + Lone + "\":[\"code\"]}}")]
    [InlineData("{\"aggregate\":{\"count\":[\"" + Lone + "\"]}}")]
    [InlineData("{\"search\":\"" + Lone + "\"}")]
    [InlineData("{\"fields\":[\"" + Lone + "\"]}")]
    [InlineData("{\"sort\":[\"" + Lone + "\"]}")]
    [InlineData("{\"facets\":[\"" + Lone + "\"]}")]
    [InlineData("\"" + Lone + "\"")]
    public async Task Text_that_cannot_be_read_is_a_400(string body)
    {
        var (status, text) = await PostAsync(body);
        status.Should().Be(HttpStatusCode.BadRequest, text);
        text.Should().Contain("BAD_USER_INPUT").And.Contain("not valid text");
    }

    [Fact]
    public async Task A_long_key_that_ends_in_a_lone_surrogate_is_a_400()
    {
        var (status, text) = await PostAsync("{\"" + new string('k', 80) + Lone + "\":1}");
        status.Should().Be(HttpStatusCode.BadRequest, text);
        text.Should().Contain("not valid text");
    }

    [Fact]
    public async Task A_valid_surrogate_pair_in_a_value_and_in_a_name_is_read()
    {
        var (status, text) = await PostAsync("{\"search\":\"\\uD83D\\uDE00\"}");
        status.Should().Be(HttpStatusCode.OK, text);
        var (unknown, unknownText) = await PostAsync("{\"\\uD83D\\uDE00\":1}");
        unknown.Should().Be(HttpStatusCode.BadRequest, unknownText);
        unknownText.Should().Contain("Unknown key");
    }

    [Fact]
    public async Task A_body_with_every_accepted_key_is_answered()
    {
        var (status, text) = await PostAsync(
            """{"fields":["code"],"filter":{"code":{"_eq":"en"}},"sort":["code"],"limit":5,"offset":0,"search":"en","deep":{},"facets":[],"aggregate":{}}""");
        status.Should().Be(HttpStatusCode.OK, text);
        var (deepStatus, deepText) = await PostAsync(
            """{"deep":{"articles":{"fields":["status"],"filter":{"status":{"_eq":"draft"}},"sort":["status"],"limit":2,"offset":0,"deep":{}}}}""",
            "fxCategory");
        deepStatus.Should().Be(HttpStatusCode.OK, deepText);
    }
}
