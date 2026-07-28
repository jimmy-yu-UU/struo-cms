using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using AwesomeAssertions;
using NetVips;
using Struo.Tests.Support;
using Xunit;

namespace Struo.Tests.Files;

[Collection("ApiIntegration")]
public class ImageTransformEndpointTests(ApiFactory factory)
{
    private readonly ApiFactory _factory = factory;
    private static JsonElement Root(string b) => JsonDocument.Parse(b).RootElement;

    private async Task<string> UploadPng(System.Net.Http.HttpClient c, int w, int h)
    {
        var bytes = Image.Black(w, h).Cast(Enums.BandFormat.Uchar).WriteToBuffer(".png");
        var content = new ByteArrayContent(bytes);
        content.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        var mp = new MultipartFormDataContent { { content, "file", "img.png" } };
        var resp = await c.PostAsync("/api/files", mp);
        return Root(await resp.Content.ReadAsStringAsync()).GetProperty("data").GetProperty("id").GetString()!;
    }

    [Fact]
    public async Task Width_param_returns_resized_image()
    {
        var c = await _factory.CreateAuthenticatedClientAsync();
        var id = await UploadPng(c, 200, 100);           // published by default
        var resp = await c.GetAsync($"/api/files/{id}/content?width=100&format=png");
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var outBytes = await resp.Content.ReadAsByteArrayAsync();
        using var img = Image.NewFromBuffer(outBytes);
        img.Width.Should().Be(100);
        img.Height.Should().Be(50);
    }

    [Fact]
    public async Task Width_over_max_is_clamped()
    {
        var c = await _factory.CreateAuthenticatedClientAsync();
        var id = await UploadPng(c, 200, 100);
        var resp = await c.GetAsync($"/api/files/{id}/content?width=999999&format=png");
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        using var img = Image.NewFromBuffer(await resp.Content.ReadAsByteArrayAsync());
        img.Width.Should().BeLessThanOrEqualTo(4096);
    }

    [Fact]
    public async Task Nonimage_ignores_transform_params()
    {
        var c = await _factory.CreateAuthenticatedClientAsync();
        var txt = new ByteArrayContent(System.Text.Encoding.UTF8.GetBytes("payload"));
        txt.Headers.ContentType = new MediaTypeHeaderValue("text/plain");
        var mp = new MultipartFormDataContent { { txt, "file", "f.txt" } };
        var up = await c.PostAsync("/api/files", mp);
        var id = Root(await up.Content.ReadAsStringAsync()).GetProperty("data").GetProperty("id").GetString();
        var resp = await c.GetAsync($"/api/files/{id}/content?width=50");
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        (await resp.Content.ReadAsStringAsync()).Should().Be("payload");   // 原檔直通
    }

    [Fact]
    public async Task Transform_preserves_published_anonymous_access()
    {
        var c = await _factory.CreateAuthenticatedClientAsync();
        var id = await UploadPng(c, 60, 60);
        var anon = _factory.CreateClient();
        (await anon.GetAsync($"/api/files/{id}/content?width=30&format=webp")).StatusCode
            .Should().Be(HttpStatusCode.OK);
    }
}
