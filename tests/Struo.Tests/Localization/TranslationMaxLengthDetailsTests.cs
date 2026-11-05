using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AwesomeAssertions;
using Struo.Tests.Support;
using Xunit;

namespace Struo.Tests.Localization;

/// <summary>
/// A translation value over its MaxLength answers with one detail carrying <c>field</c>, <c>locale</c>
/// and the unchanged message, on REST (<c>error.details</c>) and on GraphQL (the <c>details</c> extension).
/// </summary>
[Collection("ApiIntegration")]
public class TranslationMaxLengthDetailsTests(ApiFactory factory)
{
    private const string ExpectedMessage = "Field 'title' exceeds maximum length 255 for locale 'zh-TW'.";
    private readonly ApiFactory _factory = factory;
    private static JsonElement Root(string b) => JsonDocument.Parse(b).RootElement;

    [Fact]
    public async Task Rest_create_reports_the_overlong_field_and_locale()
    {
        var c = await _factory.CreateAuthenticatedClientAsync();
        var body = JsonSerializer.SerializeToElement(new
        {
            status = "draft",
            translations = new Dictionary<string, object>
            {
                ["en"] = new { title = "ok" },
                ["zh-TW"] = new { title = new string('x', 256) },
            },
        });

        var resp = await c.PostAsJsonAsync("/api/items/fxArticle", body);

        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var error = Root(await resp.Content.ReadAsStringAsync()).GetProperty("error");
        error.GetProperty("message").GetString().Should().Be(ExpectedMessage);
        var detail = error.GetProperty("details").EnumerateArray().Single();
        detail.EnumerateObject().Select(p => p.Name).Should().BeEquivalentTo(["field", "message", "locale"]);
        detail.GetProperty("field").GetString().Should().Be("title");
        detail.GetProperty("locale").GetString().Should().Be("zh-TW");
        detail.GetProperty("message").GetString().Should().Be(ExpectedMessage);
    }

    [Fact]
    public async Task Graphql_create_carries_the_details_extension()
    {
        var c = await _factory.CreateAuthenticatedClientAsync();
        var title = new string('x', 256);

        var resp = await c.PostAsJsonAsync("/graphql", new
        {
            query = "mutation { createFxArticle(input: { translations: [ " +
                    "{ locale: \"en\", fields: { title: \"ok\" } }, " +
                    $"{{ locale: \"zh-TW\", fields: {{ title: \"{title}\" }} }} ] }}) {{ id }} }}",
        });

        var err = Root(await resp.Content.ReadAsStringAsync()).GetProperty("errors")[0];
        var detail = err.GetProperty("extensions").GetProperty("details").EnumerateArray().Single();
        detail.GetProperty("field").GetString().Should().Be("title");
        detail.GetProperty("locale").GetString().Should().Be("zh-TW");
        detail.GetProperty("message").GetString().Should().Be(ExpectedMessage);
    }
}
