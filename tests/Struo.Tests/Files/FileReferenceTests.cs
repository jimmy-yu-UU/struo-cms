using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using AwesomeAssertions;
using Struo.Tests.Support;
using Xunit;

namespace Struo.Tests.Files;

[Collection("ApiIntegration")]
public class FileReferenceTests(ApiFactory factory)
{
    private readonly ApiFactory _factory = factory;
    private static JsonElement Root(string b) => JsonDocument.Parse(b).RootElement;

    private async Task<string> UploadFile(System.Net.Http.HttpClient c)
    {
        var content = new ByteArrayContent([9, 9, 9]);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        var mp = new MultipartFormDataContent { { content, "file", "img.bin" } };
        var resp = await c.PostAsync("/api/files", mp);
        return Root(await resp.Content.ReadAsStringAsync()).GetProperty("data").GetProperty("id").GetString()!;
    }

    private async Task<long> NewAuthor(System.Net.Http.HttpClient c) =>
        Root(await (await c.PostAsJsonAsync("/api/items/author", new { name = "RefA" })).Content.ReadAsStringAsync())
            .GetProperty("data").GetProperty("id").GetInt64();

    [Fact]
    public async Task Gallery_m2m_with_guid_file_ids_round_trips()
    {
        var c = _factory.CreateClient();
        var f1 = await UploadFile(c);
        var f2 = await UploadFile(c);
        var author = await NewAuthor(c);

        var body = JsonSerializer.SerializeToElement(new
        {
            status = "draft", authorId = author,
            translations = new Dictionary<string, object> { ["en"] = new { title = "A", body = (string?)null } },
            gallery = new[] { f1, f2 }
        });
        var id = Root(await (await c.PostAsJsonAsync("/api/items/article", body)).Content.ReadAsStringAsync())
            .GetProperty("data").GetProperty("id").GetInt64();

        var data = Root(await (await c.GetAsync($"/api/items/article/{id}?deep=gallery")).Content.ReadAsStringAsync())
            .GetProperty("data");
        data.GetProperty("gallery").EnumerateArray().Select(g => g.GetProperty("id").GetString())
            .Should().BeEquivalentTo(new[] { f1, f2 });
    }

    [Fact]
    public async Task Single_image_relation_expands()
    {
        var c = _factory.CreateClient();
        var f1 = await UploadFile(c);
        var author = await NewAuthor(c);

        var body = JsonSerializer.SerializeToElement(new
        {
            status = "draft", authorId = author, seoOgImageId = f1,
            translations = new Dictionary<string, object> { ["en"] = new { title = "B", body = (string?)null } }
        });
        var id = Root(await (await c.PostAsJsonAsync("/api/items/article", body)).Content.ReadAsStringAsync())
            .GetProperty("data").GetProperty("id").GetInt64();

        var data = Root(await (await c.GetAsync($"/api/items/article/{id}?deep=seoOgImage")).Content.ReadAsStringAsync())
            .GetProperty("data");
        data.GetProperty("seoOgImage").GetProperty("id").GetString().Should().Be(f1);
    }
}
