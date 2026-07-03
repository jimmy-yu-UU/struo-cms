using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using AwesomeAssertions;
using Struo.Tests.Support;
using Xunit;

namespace Struo.Tests.Files;

[Collection("ApiIntegration")]
public class FileUploadTests(ApiFactory factory)
{
    private readonly ApiFactory _factory = factory;
    private static JsonElement Root(string b) => JsonDocument.Parse(b).RootElement;

    private static MultipartFormDataContent Multipart(byte[] bytes, string name, string contentType)
    {
        var content = new ByteArrayContent(bytes);
        content.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        return new MultipartFormDataContent { { content, "file", name } };
    }

    [Fact]
    public async Task Upload_returns_201_with_metadata_and_published_status()
    {
        // Uploads default to "published": dimensions are extracted synchronously in the same
        // call, so there is no pending async step that "draft" was gating (7e live-gate finding).
        var c = await _factory.CreateAuthenticatedClientAsync();
        var resp = await c.PostAsync("/api/files", Multipart(Encoding.UTF8.GetBytes("hello world"), "note.txt", "text/plain"));
        resp.StatusCode.Should().Be(HttpStatusCode.Created);
        var data = Root(await resp.Content.ReadAsStringAsync()).GetProperty("data");
        data.GetProperty("fileName").GetString().Should().Be("note.txt");
        data.GetProperty("contentType").GetString().Should().Be("text/plain");
        data.GetProperty("size").GetInt64().Should().Be(11);
        data.GetProperty("status").GetString().Should().Be("published");
        Guid.TryParse(data.GetProperty("id").GetString(), out _).Should().BeTrue();
    }

    [Fact]
    public async Task Upload_multibyte_filename_round_trips()
    {
        var c = await _factory.CreateAuthenticatedClientAsync();
        var resp = await c.PostAsync("/api/files", Multipart([1, 2, 3], "報告.bin", "application/octet-stream"));
        var data = Root(await resp.Content.ReadAsStringAsync()).GetProperty("data");
        data.GetProperty("fileName").GetString().Should().Be("報告.bin");
    }

    [Fact]
    public async Task Upload_image_populates_width_height()
    {
        var c = await _factory.CreateAuthenticatedClientAsync();
        // 1x1 PNG
        var png = Convert.FromBase64String(
            "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg==");
        var resp = await c.PostAsync("/api/files", Multipart(png, "px.png", "image/png"));
        var data = Root(await resp.Content.ReadAsStringAsync()).GetProperty("data");
        data.GetProperty("width").GetInt32().Should().Be(1);
        data.GetProperty("height").GetInt32().Should().Be(1);
    }
}
