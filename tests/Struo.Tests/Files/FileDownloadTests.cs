using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using AwesomeAssertions;
using Struo.Tests.Support;
using Xunit;

namespace Struo.Tests.Files;

[Collection("ApiIntegration")]
public class FileDownloadTests(ApiFactory factory)
{
    private readonly ApiFactory _factory = factory;
    private static JsonElement Root(string b) => JsonDocument.Parse(b).RootElement;

    private async Task<string> UploadDraft(System.Net.Http.HttpClient c, string body = "data")
    {
        var content = new ByteArrayContent(Encoding.UTF8.GetBytes(body));
        content.Headers.ContentType = new MediaTypeHeaderValue("text/plain");
        var mp = new MultipartFormDataContent { { content, "file", "f.txt" } };
        var resp = await c.PostAsync("/api/files", mp);
        return Root(await resp.Content.ReadAsStringAsync()).GetProperty("data").GetProperty("id").GetString()!;
    }

    private async Task<System.Net.Http.HttpResponseMessage> Publish(System.Net.Http.HttpClient c, string id) =>
        await c.PutAsJsonAsync($"/api/items/file/{id}", new
        {
            status = "published",
            translations = new Dictionary<string, object> { ["en"] = new { title = "T", alt = (string?)null } }
        });

    [Fact]
    public async Task Draft_download_is_404()
    {
        var c = await _factory.CreateAuthenticatedClientAsync();
        var id = await UploadDraft(c);
        (await c.GetAsync($"/api/files/{id}/content")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await c.GetAsync($"/api/files/{id}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Published_download_streams_bytes()
    {
        var c = await _factory.CreateAuthenticatedClientAsync();
        var id = await UploadDraft(c, "payload");
        await Publish(c, id);
        var resp = await c.GetAsync($"/api/files/{id}/content");
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        (await resp.Content.ReadAsStringAsync()).Should().Be("payload");
    }

    [Fact]
    public async Task Published_info_returns_metadata()
    {
        var c = await _factory.CreateAuthenticatedClientAsync();
        var id = await UploadDraft(c);
        var pub = await Publish(c, id);
        pub.StatusCode.Should().Be(HttpStatusCode.OK, await pub.Content.ReadAsStringAsync());
        var resp = await c.GetAsync($"/api/files/{id}");
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        Root(await resp.Content.ReadAsStringAsync()).GetProperty("data").GetProperty("status").GetString()
            .Should().Be("published");
    }

    [Fact]
    public async Task Delete_removes_file_then_download_404()
    {
        var c = await _factory.CreateAuthenticatedClientAsync();
        var id = await UploadDraft(c);
        await Publish(c, id);
        (await c.DeleteAsync($"/api/files/{id}")).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await c.GetAsync($"/api/files/{id}/content")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
