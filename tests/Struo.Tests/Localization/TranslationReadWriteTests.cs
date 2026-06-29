using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AwesomeAssertions;
using Struo.Tests.Support;
using Xunit;

namespace Struo.Tests.Localization;

[Collection("ApiIntegration")]
public class TranslationReadWriteTests(ApiFactory factory)
{
    private readonly ApiFactory _factory = factory;
    private static JsonElement Root(string b) => JsonDocument.Parse(b).RootElement;

    [Fact]
    public async Task Create_with_translations_and_read_all_locales()
    {
        var c = _factory.CreateClient();
        var body = JsonSerializer.SerializeToElement(new
        {
            status = "draft",
            translations = new Dictionary<string, object>
            {
                ["en"] = new { title = "Hello", body = "B-en" },
                ["zh-TW"] = new { title = "你好", body = "B-zh" }
            }
        });
        var id = Root(await (await c.PostAsJsonAsync("/api/items/article", body)).Content.ReadAsStringAsync())
            .GetProperty("data").GetProperty("id").GetString()!;

        var all = Root(await (await c.GetAsync($"/api/items/article/{id}")).Content.ReadAsStringAsync()).GetProperty("data");
        var tr = all.GetProperty("translations");
        tr.GetProperty("en").GetProperty("title").GetString().Should().Be("Hello");
        tr.GetProperty("zh-TW").GetProperty("title").GetString().Should().Be("你好");
        all.TryGetProperty("title", out _).Should().BeFalse();
    }

    [Fact]
    public async Task Read_single_locale_filters_translations()
    {
        var c = _factory.CreateClient();
        var body = JsonSerializer.SerializeToElement(new
        {
            status = "draft",
            translations = new Dictionary<string, object>
            {
                ["en"] = new { title = "OnlyEn", body = (string?)null },
                ["zh-TW"] = new { title = "只中", body = (string?)null }
            }
        });
        var id = Root(await (await c.PostAsJsonAsync("/api/items/article", body)).Content.ReadAsStringAsync())
            .GetProperty("data").GetProperty("id").GetString()!;

        var tr = Root(await (await c.GetAsync($"/api/items/article/{id}?locale=zh-TW")).Content.ReadAsStringAsync())
            .GetProperty("data").GetProperty("translations");
        tr.TryGetProperty("zh-TW", out _).Should().BeTrue();
        tr.TryGetProperty("en", out _).Should().BeFalse();
    }

    [Fact]
    public async Task Partial_update_preserves_other_locale()
    {
        var c = _factory.CreateClient();
        var body = JsonSerializer.SerializeToElement(new
        {
            status = "draft",
            translations = new Dictionary<string, object>
            {
                ["en"] = new { title = "E1", body = (string?)null },
                ["zh-TW"] = new { title = "Z1", body = (string?)null }
            }
        });
        var id = Root(await (await c.PostAsJsonAsync("/api/items/article", body)).Content.ReadAsStringAsync())
            .GetProperty("data").GetProperty("id").GetString()!;

        var upd = JsonSerializer.SerializeToElement(new
        {
            status = "draft",
            translations = new Dictionary<string, object> { ["zh-TW"] = new { title = "Z2", body = (string?)null } }
        });
        await c.PutAsJsonAsync($"/api/items/article/{id}", upd);

        var tr = Root(await (await c.GetAsync($"/api/items/article/{id}")).Content.ReadAsStringAsync())
            .GetProperty("data").GetProperty("translations");
        tr.GetProperty("zh-TW").GetProperty("title").GetString().Should().Be("Z2");
        tr.GetProperty("en").GetProperty("title").GetString().Should().Be("E1");
    }

    [Fact]
    public async Task Unknown_locale_on_write_is_400()
    {
        var c = _factory.CreateClient();
        var body = JsonSerializer.SerializeToElement(new
        {
            status = "draft",
            translations = new Dictionary<string, object> { ["xx"] = new { title = "x", body = (string?)null } }
        });
        (await c.PostAsJsonAsync("/api/items/article", body)).StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }
}
