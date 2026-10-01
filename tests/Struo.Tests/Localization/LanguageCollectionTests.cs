using System.Linq;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AwesomeAssertions;
using Struo.Tests.Support;
using Xunit;

namespace Struo.Tests.Localization;

[Collection("ApiIntegration")]
public class LanguageCollectionTests(ApiFactory factory)
{
    private readonly ApiFactory _factory = factory;
    private static JsonElement Root(string b) => JsonDocument.Parse(b).RootElement;

    [Fact]
    public async Task Language_collection_is_registered_in_schema()
    {
        var c = await _factory.CreateAuthenticatedClientAsync(); // /api/schema now requires auth
        var resp = await c.GetAsync("/api/schema/language");
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        Root(await resp.Content.ReadAsStringAsync()).GetProperty("data").GetProperty("fields")
            .EnumerateArray().Select(f => f.GetProperty("name").GetString())
            .Should().Contain(new[] { "code", "name", "isDefault", "enabled" });
    }

    [Fact]
    public async Task Language_supports_crud()
    {
        var c = await _factory.CreateAuthenticatedClientAsync();
        var created = Root(await (await c.PostAsJsonAsync("/api/items/language",
            new { code = "fr", name = "Français", isDefault = false, enabled = true, sort = 9 }))
            .Content.ReadAsStringAsync()).GetProperty("data");
        created.GetProperty("code").GetString().Should().Be("fr");

        var list = Root(await (await c.GetAsync("/api/items/language?limit=100")).Content.ReadAsStringAsync())
            .GetProperty("data");
        list.EnumerateArray().Select(r => r.GetProperty("code").GetString()).Should().Contain("fr");
    }

    private static async Task<JsonElement> ListAsync(HttpClient c) =>
        Root(await (await c.GetAsync("/api/items/language?limit=100")).Content.ReadAsStringAsync()).GetProperty("data");

    private static string IdOf(JsonElement list, string code) =>
        list.EnumerateArray().Single(r => r.GetProperty("code").GetString() == code).GetProperty("id").ToString();

    [Fact]
    public async Task Creating_a_language_with_an_existing_code_is_a_400()
    {
        var c = await _factory.CreateAuthenticatedClientAsync();
        var res = await c.PostAsJsonAsync("/api/items/language",
            new { code = "EN", name = "English again", isDefault = false, enabled = true, sort = 50 });
        res.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        Root(await res.Content.ReadAsStringAsync()).GetProperty("error").GetProperty("message").GetString()
            .Should().Be("Language code 'EN' already exists.");
    }

    [Fact]
    public async Task Making_a_second_language_the_default_is_a_400()
    {
        var c = await _factory.CreateAuthenticatedClientAsync();
        var id = IdOf(await ListAsync(c), "zh-TW");
        var res = await c.PutAsJsonAsync($"/api/items/language/{id}",
            new { code = "zh-TW", name = "繁體中文", isDefault = true, enabled = true, sort = 2 });
        res.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        Root(await res.Content.ReadAsStringAsync()).GetProperty("error").GetProperty("message").GetString()
            .Should().Be("Exactly one enabled language must be the default.");
    }

    [Fact]
    public async Task Disabling_the_default_language_is_a_400_and_rolls_back()
    {
        var c = await _factory.CreateAuthenticatedClientAsync();
        var id = IdOf(await ListAsync(c), "en");
        var res = await c.PutAsJsonAsync($"/api/items/language/{id}",
            new { code = "en", name = "English", isDefault = true, enabled = false, sort = 1 });
        res.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var after = await ListAsync(c);
        after.EnumerateArray().Single(r => r.GetProperty("code").GetString() == "en")
            .GetProperty("enabled").GetBoolean().Should().BeTrue("the transaction must roll back");
    }

    [Fact]
    public async Task Deleting_a_non_default_language_succeeds_and_deleting_the_default_is_a_400()
    {
        var c = await _factory.CreateAuthenticatedClientAsync();
        var created = Root(await (await c.PostAsJsonAsync("/api/items/language",
            new { code = "it", name = "Italiano", isDefault = false, enabled = true, sort = 30 })).Content.ReadAsStringAsync())
            .GetProperty("data").GetProperty("id").ToString();
        (await c.DeleteAsync($"/api/items/language/{created}")).StatusCode.Should().Be(HttpStatusCode.NoContent);

        var enId = IdOf(await ListAsync(c), "en");
        var res = await c.DeleteAsync($"/api/items/language/{enId}");
        res.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await ListAsync(c)).EnumerateArray().Select(r => r.GetProperty("code").GetString()).Should().Contain("en");
    }
}
