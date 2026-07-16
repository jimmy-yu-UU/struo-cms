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
        var c = await _factory.CreateAuthenticatedClientAsync();
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
        var c = await _factory.CreateAuthenticatedClientAsync();
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
        var c = await _factory.CreateAuthenticatedClientAsync();
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
        var c = await _factory.CreateAuthenticatedClientAsync();
        var body = JsonSerializer.SerializeToElement(new
        {
            status = "draft",
            translations = new Dictionary<string, object> { ["xx"] = new { title = "x", body = (string?)null } }
        });
        (await c.PostAsJsonAsync("/api/items/article", body)).StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Article_seo_is_per_locale()
    {
        var c = await _factory.CreateAuthenticatedClientAsync();
        var body = JsonSerializer.SerializeToElement(new
        {
            status = "draft",
            translations = new Dictionary<string, object>
            {
                ["en"]    = new { title = "Hello",  seoTitle = "Hello SEO",  seoMetaDescription = "en desc" },
                ["zh-TW"] = new { title = "哈囉", seoTitle = "哈囉 SEO", seoMetaDescription = "zh desc" }
            }
        });
        var id = Root(await (await c.PostAsJsonAsync("/api/items/article", body)).Content.ReadAsStringAsync())
            .GetProperty("data").GetProperty("id").GetString()!;

        var tr = Root(await (await c.GetAsync($"/api/items/article/{id}")).Content.ReadAsStringAsync())
            .GetProperty("data").GetProperty("translations");
        tr.GetProperty("en").GetProperty("seoTitle").GetString().Should().Be("Hello SEO");
        tr.GetProperty("en").GetProperty("seoMetaDescription").GetString().Should().Be("en desc");
        tr.GetProperty("zh-TW").GetProperty("seoTitle").GetString().Should().Be("哈囉 SEO");
        tr.GetProperty("zh-TW").GetProperty("seoMetaDescription").GetString().Should().Be("zh desc");
    }

    [Fact]
    public async Task Per_locale_og_image_resolves_to_file_object()
    {
        var c = await _factory.CreateAuthenticatedClientAsync();

        // 1. Upload a file, capture its id
        var content = new System.Net.Http.ByteArrayContent(new byte[] { 1, 2, 3 });
        content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("text/plain"); // whitelisted (SEC-6)
        var mp = new System.Net.Http.MultipartFormDataContent { { content, "file", "og.txt" } };
        var fileResp = await c.PostAsync("/api/files", mp);
        var fileId = Root(await fileResp.Content.ReadAsStringAsync()).GetProperty("data").GetProperty("id").GetString()!;
        var fileGuid = Guid.Parse(fileId);

        // 2. Create article: en has seoOgImageId, zh-TW has none
        var body = System.Text.Json.JsonSerializer.SerializeToElement(new
        {
            status = "draft",
            translations = new Dictionary<string, object>
            {
                ["en"]    = new { title = "OG Test", seoOgImageId = fileGuid },
                ["zh-TW"] = new { title = "OG 測試" }
            }
        });
        var articleId = Root(await (await c.PostAsJsonAsync("/api/items/article", body)).Content.ReadAsStringAsync())
            .GetProperty("data").GetProperty("id").GetString()!;

        // 3. GET the article (all locales)
        var tr = Root(await (await c.GetAsync($"/api/items/article/{articleId}")).Content.ReadAsStringAsync())
            .GetProperty("data").GetProperty("translations");

        // 4. Assert
        tr.GetProperty("en").GetProperty("seoOgImage").ValueKind.Should().NotBe(System.Text.Json.JsonValueKind.Null);
        tr.GetProperty("en").GetProperty("seoOgImage").GetProperty("id").GetString().Should().Be(fileId);
        tr.GetProperty("en").GetProperty("seoOgImage").TryGetProperty("fileName", out _).Should().BeTrue();
        tr.GetProperty("zh-TW").GetProperty("seoOgImage").ValueKind.Should().Be(System.Text.Json.JsonValueKind.Null);
    }

    [Fact]
    public async Task Dangling_og_image_id_resolves_to_null()
    {
        var c = await _factory.CreateAuthenticatedClientAsync();

        // Create article with a random (non-existent) file id
        var danglingId = Guid.NewGuid();
        var body = System.Text.Json.JsonSerializer.SerializeToElement(new
        {
            status = "draft",
            translations = new Dictionary<string, object>
            {
                ["en"] = new { title = "Dangling", seoOgImageId = danglingId }
            }
        });
        var articleId = Root(await (await c.PostAsJsonAsync("/api/items/article", body)).Content.ReadAsStringAsync())
            .GetProperty("data").GetProperty("id").GetString()!;

        // GET → seoOgImage must be null, no throw
        var tr = Root(await (await c.GetAsync($"/api/items/article/{articleId}")).Content.ReadAsStringAsync())
            .GetProperty("data").GetProperty("translations");
        tr.GetProperty("en").GetProperty("seoOgImage").ValueKind.Should().Be(System.Text.Json.JsonValueKind.Null);
    }
}
