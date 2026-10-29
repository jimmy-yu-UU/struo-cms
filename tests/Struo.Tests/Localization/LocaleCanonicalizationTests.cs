using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AwesomeAssertions;
using Struo.Tests.Support;
using Xunit;

namespace Struo.Tests.Localization;

// Locale codes are stored in their configured spelling: a differently-cased key in a write
// payload, or in `?locale=`, resolves to the configured code instead of creating or missing a
// case-variant row on case-sensitive backends.
[Collection("ApiIntegration")]
public class LocaleCanonicalizationTests(ApiFactory factory)
{
    private static JsonElement Root(string b) => JsonDocument.Parse(b).RootElement;

    private static JsonElement Translations(string collectionBody) =>
        Root(collectionBody).GetProperty("data").GetProperty("translations");

    [Fact]
    public async Task Differently_cased_locale_keys_write_the_same_translation_row()
    {
        var c = await factory.CreateAuthenticatedClientAsync();
        var create = JsonSerializer.SerializeToElement(new
        {
            status = "draft",
            translations = new Dictionary<string, object>
            {
                ["EN"] = new { title = "E1", body = (string?)null },
                ["ZH-tw"] = new { title = "Z1", body = (string?)null },
            },
        });
        var created = await c.PostAsJsonAsync("/api/items/fxArticle", create);
        created.StatusCode.Should().Be(HttpStatusCode.Created);
        var id = Root(await created.Content.ReadAsStringAsync()).GetProperty("data").GetProperty("id").GetString()!;

        var update = JsonSerializer.SerializeToElement(new
        {
            translations = new Dictionary<string, object> { ["zh-TW"] = new { title = "Z2", body = (string?)null } },
        });
        (await c.PutAsJsonAsync($"/api/items/fxArticle/{id}", update)).StatusCode.Should().Be(HttpStatusCode.OK);

        var tr = Translations(await (await c.GetAsync($"/api/items/fxArticle/{id}")).Content.ReadAsStringAsync());
        tr.EnumerateObject().Select(p => p.Name).Should().BeEquivalentTo(["en", "zh-TW"]);
        tr.GetProperty("zh-TW").GetProperty("title").GetString().Should().Be("Z2");
        tr.GetProperty("en").GetProperty("title").GetString().Should().Be("E1");
    }

    [Fact]
    public async Task Differently_cased_locale_query_returns_the_configured_translation()
    {
        var c = await factory.CreateAuthenticatedClientAsync();
        var create = JsonSerializer.SerializeToElement(new
        {
            status = "draft",
            translations = new Dictionary<string, object>
            {
                ["en"] = new { title = "E1", body = (string?)null },
                ["zh-TW"] = new { title = "Z1", body = (string?)null },
            },
        });
        var id = Root(await (await c.PostAsJsonAsync("/api/items/fxArticle", create)).Content.ReadAsStringAsync())
            .GetProperty("data").GetProperty("id").GetString()!;

        var tr = Translations(await (await c.GetAsync($"/api/items/fxArticle/{id}?locale=ZH-tw")).Content.ReadAsStringAsync());

        tr.GetProperty("zh-TW").GetProperty("title").GetString().Should().Be("Z1");
        tr.TryGetProperty("en", out _).Should().BeFalse();
    }
}
