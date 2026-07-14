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
        var c = _factory.CreateClient();
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
}
