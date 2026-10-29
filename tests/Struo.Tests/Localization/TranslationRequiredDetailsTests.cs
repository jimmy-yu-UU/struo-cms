using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AwesomeAssertions;
using Struo.Tests.Support;
using Xunit;

namespace Struo.Tests.Localization;

/// <summary>
/// A missing required translation field answers with one detail per missing field per locale, carrying
/// <c>field</c>, <c>locale</c> and <c>message</c>, on REST (<c>error.details</c>) and on GraphQL
/// (the <c>details</c> error extension).
/// </summary>
[Collection("ApiIntegration")]
public class TranslationRequiredDetailsTests(ApiFactory factory)
{
    private readonly ApiFactory _factory = factory;
    private static JsonElement Root(string b) => JsonDocument.Parse(b).RootElement;

    private static void AssertDetail(JsonElement detail) =>
        detail.EnumerateObject().Select(p => p.Name).Should().BeEquivalentTo(["field", "message", "locale"]);

    [Fact]
    public async Task Rest_create_reports_each_locale_missing_the_required_field()
    {
        var c = await _factory.CreateAuthenticatedClientAsync();
        var body = JsonSerializer.SerializeToElement(new
        {
            status = "draft",
            translations = new Dictionary<string, object>
            {
                ["en"] = new { body = "no title" },
                ["zh-TW"] = new { body = "no title" },
            },
        });

        var resp = await c.PostAsJsonAsync("/api/items/fxArticle", body);

        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var error = Root(await resp.Content.ReadAsStringAsync()).GetProperty("error");
        var details = error.GetProperty("details").EnumerateArray().ToList();
        details.Should().HaveCount(2);
        details.Select(d => d.GetProperty("locale").GetString()).Should().BeEquivalentTo(["en", "zh-TW"]);
        details.Should().OnlyContain(d => d.GetProperty("field").GetString() == "title");
        details.Should().OnlyContain(d => d.GetProperty("message").GetString()!.StartsWith("Required translation field 'title' is missing for locale '"));
        foreach (var d in details) AssertDetail(d);
    }

    [Fact]
    public async Task Rest_update_reports_the_locale_and_field()
    {
        var c = await _factory.CreateAuthenticatedClientAsync();
        var created = await c.PostAsJsonAsync("/api/items/fxArticle", JsonSerializer.SerializeToElement(new
        {
            status = "draft",
            translations = new { en = new { title = "A" } },
        }));
        var id = Root(await created.Content.ReadAsStringAsync()).GetProperty("data").GetProperty("id").GetString()!;

        var resp = await c.PutAsJsonAsync($"/api/items/fxArticle/{id}", JsonSerializer.SerializeToElement(new
        {
            translations = new Dictionary<string, object> { ["zh-TW"] = new { body = "only body" } },
        }));

        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var error = Root(await resp.Content.ReadAsStringAsync()).GetProperty("error");
        error.GetProperty("code").GetString().Should().Be("BAD_USER_INPUT");
        error.GetProperty("message").GetString().Should().Be("Required translation field 'title' is missing for locale 'zh-TW'.");
        var detail = error.GetProperty("details").EnumerateArray().Single();
        detail.GetProperty("field").GetString().Should().Be("title");
        detail.GetProperty("locale").GetString().Should().Be("zh-TW");
        detail.GetProperty("message").GetString().Should().Be("Required translation field 'title' is missing for locale 'zh-TW'.");
    }

    [Fact]
    public async Task Rest_error_without_details_omits_the_details_key()
    {
        var c = await _factory.CreateAuthenticatedClientAsync();

        var resp = await c.PostAsJsonAsync("/api/items/fxArticle", JsonSerializer.SerializeToElement(new { status = "draft" }));

        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        Root(await resp.Content.ReadAsStringAsync()).GetProperty("error").TryGetProperty("details", out _).Should().BeFalse();
    }

    [Fact]
    public async Task Graphql_create_carries_the_details_extension()
    {
        var c = await _factory.CreateAuthenticatedClientAsync();

        var resp = await c.PostAsJsonAsync("/graphql", new
        {
            query = "mutation { createFxArticle(input: { translations: [ { locale: \"en\", fields: { body: \"x\" } } ] }) { id } }",
        });

        var err = Root(await resp.Content.ReadAsStringAsync()).GetProperty("errors")[0];
        err.GetProperty("extensions").GetProperty("code").GetString().Should().Be("BAD_USER_INPUT");
        var detail = err.GetProperty("extensions").GetProperty("details").EnumerateArray().Single();
        detail.GetProperty("field").GetString().Should().Be("title");
        detail.GetProperty("locale").GetString().Should().Be("en");
        detail.GetProperty("message").GetString().Should().Be("Required translation field 'title' is missing for locale 'en'.");
    }
}
