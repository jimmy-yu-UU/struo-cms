using System.Net.Http.Json;
using System.Text.Json;
using AwesomeAssertions;
using Struo.Tests.Support;
using Xunit;

namespace Struo.Tests.Localization;

/// <summary>
/// Guard tests for the live-PostgreSQL bug where multibyte (e.g. Chinese) translation
/// values threw "Cannot transcode invalid UTF-8 JSON text" because the controller read a
/// pooled-buffer-backed <see cref="JsonElement"/> AFTER an await (Kestrel recycled the buffer).
/// The fix is a <c>body.Clone()</c> at the controller boundary.
///
/// NOTE: These may pass even WITHOUT the fix under the in-process TestServer, which does not
/// recycle the request buffer the same way Kestrel does. They are documentation/guard tests,
/// not a live Kestrel repro. Live Postgres re-verification is still required.
/// </summary>
[Collection("ApiIntegration")]
public class TranslationMultibyteBufferTests(ApiFactory factory)
{
    private readonly ApiFactory _factory = factory;
    private static JsonElement Root(string b) => JsonDocument.Parse(b).RootElement;
    private static object Eq(object v) => new Dictionary<string, object> { ["_eq"] = v };

    private async Task<long> NewAuthor(System.Net.Http.HttpClient c, string n) =>
        Root(await (await c.PostAsJsonAsync("/api/items/author", new { name = n })).Content.ReadAsStringAsync())
            .GetProperty("data").GetProperty("id").GetInt64();

    [Fact]
    public async Task Create_with_multibyte_translation_round_trips()
    {
        var c = _factory.CreateClient();
        var author = await NewAuthor(c, "MB-A");
        var body = JsonSerializer.SerializeToElement(new
        {
            status = "draft", authorId = author,
            translations = new Dictionary<string, object>
            {
                ["en"] = new { title = "Hello", body = "English body" },
                ["zh-TW"] = new { title = "你好", body = "這是一段中文內容，包含多位元組字元。" }
            }
        });

        var id = Root(await (await c.PostAsJsonAsync("/api/items/article", body)).Content.ReadAsStringAsync())
            .GetProperty("data").GetProperty("id").GetInt64();

        var tr = Root(await (await c.GetAsync($"/api/items/article/{id}")).Content.ReadAsStringAsync())
            .GetProperty("data").GetProperty("translations");
        tr.GetProperty("en").GetProperty("title").GetString().Should().Be("Hello");
        tr.GetProperty("zh-TW").GetProperty("title").GetString().Should().Be("你好");
        tr.GetProperty("zh-TW").GetProperty("body").GetString().Should().Be("這是一段中文內容，包含多位元組字元。");
    }

    [Fact]
    public async Task Filter_translatable_field_with_multibyte_value_returns_match()
    {
        var c = _factory.CreateClient();
        var author = await NewAuthor(c, "MB-B");
        var body = JsonSerializer.SerializeToElement(new
        {
            status = "draft", authorId = author,
            translations = new Dictionary<string, object>
            {
                ["en"] = new { title = "Findable", body = (string?)null },
                ["zh-TW"] = new { title = "獨特標題", body = (string?)null }
            }
        });
        var hit = Root(await (await c.PostAsJsonAsync("/api/items/article", body)).Content.ReadAsStringAsync())
            .GetProperty("data").GetProperty("id").GetInt64();

        var env = JsonSerializer.SerializeToElement(new
        {
            filter = new Dictionary<string, object> { ["title"] = Eq("獨特標題") }
        });
        var data = Root(await (await c.PostAsJsonAsync("/api/items/article/query?locale=zh-TW", env)).Content.ReadAsStringAsync())
            .GetProperty("data");
        data.EnumerateArray().Select(r => r.GetProperty("id").GetInt64()).Should().Contain(hit);
    }
}
