using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using AwesomeAssertions;
using Struo.Tests.Support;
using Xunit;

namespace Struo.Tests.Files;

[Collection("ApiIntegration")]
public class FileTranslationTests(ApiFactory factory)
{
    private readonly ApiFactory _factory = factory;
    private static JsonElement Root(string b) => JsonDocument.Parse(b).RootElement;

    private async Task<string> Upload(System.Net.Http.HttpClient c)
    {
        var content = new ByteArrayContent([1, 2, 3]);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        var mp = new MultipartFormDataContent { { content, "file", "a.bin" } };
        var resp = await c.PostAsync("/api/files", mp);
        return Root(await resp.Content.ReadAsStringAsync()).GetProperty("data").GetProperty("id").GetString()!;
    }

    [Fact]
    public async Task Title_alt_translations_round_trip_all_locales()
    {
        var c = await _factory.CreateAuthenticatedClientAsync();
        var id = await Upload(c);
        await c.PutAsJsonAsync($"/api/items/file/{id}", new
        {
            status = "published",
            translations = new Dictionary<string, object>
            {
                ["en"] = new { title = "Hello", alt = "An image" },
                ["zh-TW"] = new { title = "你好", alt = "圖片" }
            }
        });

        var data = Root(await (await c.GetAsync($"/api/items/file/{id}")).Content.ReadAsStringAsync()).GetProperty("data");
        var tr = data.GetProperty("translations");
        tr.GetProperty("en").GetProperty("title").GetString().Should().Be("Hello");
        tr.GetProperty("zh-TW").GetProperty("alt").GetString().Should().Be("圖片");
        data.TryGetProperty("title", out _).Should().BeFalse(); // title is not a top-level field
    }

    [Fact]
    public async Task Single_locale_filter_on_file_translations()
    {
        var c = await _factory.CreateAuthenticatedClientAsync();
        var id = await Upload(c);
        await c.PutAsJsonAsync($"/api/items/file/{id}", new
        {
            status = "published",
            translations = new Dictionary<string, object>
            {
                ["en"] = new { title = "EnOnly", alt = (string?)null },
                ["zh-TW"] = new { title = "只中", alt = (string?)null }
            }
        });
        var tr = Root(await (await c.GetAsync($"/api/items/file/{id}?locale=zh-TW")).Content.ReadAsStringAsync())
            .GetProperty("data").GetProperty("translations");
        tr.TryGetProperty("zh-TW", out _).Should().BeTrue();
        tr.TryGetProperty("en", out _).Should().BeFalse();
    }
}
