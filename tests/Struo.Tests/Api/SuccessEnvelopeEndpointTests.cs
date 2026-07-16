using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AwesomeAssertions;
using Struo.Tests.Support;
using Xunit;

namespace Struo.Tests.Api;

[Collection("ApiIntegration")]
public class SuccessEnvelopeEndpointTests(ApiFactory factory)
{
    private readonly ApiFactory _factory = factory;
    private static JsonElement Root(string body) => JsonDocument.Parse(body).RootElement;

    [Fact]
    public async Task List_has_success_data_and_meta()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();
        var resp = await client.GetAsync("/api/items/article?limit=2&offset=0");
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var root = Root(await resp.Content.ReadAsStringAsync());
        root.GetProperty("success").GetBoolean().Should().BeTrue();
        root.GetProperty("data").ValueKind.Should().Be(JsonValueKind.Array);
        var meta = root.GetProperty("meta");
        meta.GetProperty("total").GetInt64().Should().BeGreaterThanOrEqualTo(0);
        meta.TryGetProperty("limit", out _).Should().BeTrue();
        meta.TryGetProperty("offset", out _).Should().BeTrue();
    }

    [Fact]
    public async Task Create_get_are_success_data_without_meta()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();
        var create = await client.PostAsJsonAsync("/api/items/article",
            new { status = "draft", translations = new { en = new { title = "EnvOk" } } });
        create.StatusCode.Should().Be(HttpStatusCode.Created);
        var created = Root(await create.Content.ReadAsStringAsync());
        created.GetProperty("success").GetBoolean().Should().BeTrue();
        created.TryGetProperty("meta", out _).Should().BeFalse();
        var id = created.GetProperty("data").GetProperty("id").GetString()!;

        var get = await client.GetAsync($"/api/items/article/{id}");
        var got = Root(await get.Content.ReadAsStringAsync());
        got.GetProperty("success").GetBoolean().Should().BeTrue();
        got.GetProperty("data").GetProperty("id").GetString().Should().Be(id);
    }

    [Fact]
    public async Task Delete_stays_bare_204()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();
        var create = await client.PostAsJsonAsync("/api/items/article",
            new { status = "draft", translations = new { en = new { title = "ToDelete" } } });
        var id = Root(await create.Content.ReadAsStringAsync()).GetProperty("data").GetProperty("id").GetString()!;
        var del = await client.DeleteAsync($"/api/items/article/{id}?purge=true");
        del.StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await del.Content.ReadAsStringAsync()).Should().BeEmpty();
    }

    [Fact]
    public async Task File_content_is_not_enveloped()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();
        using var form = new MultipartFormDataContent();
        var bytes = new byte[] { 1, 2, 3, 4 };
        var part = new ByteArrayContent(bytes);
        part.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("text/plain"); // whitelisted (SEC-6)
        form.Add(part, "file", "blob.txt");
        var upload = await client.PostAsync("/api/files", form);
        upload.StatusCode.Should().Be(HttpStatusCode.Created);
        var id = Root(await upload.Content.ReadAsStringAsync()).GetProperty("data").GetProperty("id").GetString()!;

        var content = await client.GetAsync($"/api/files/{id}/content");
        content.StatusCode.Should().Be(HttpStatusCode.OK); // local storage streams the bytes (no presigned redirect)
        (await content.Content.ReadAsByteArrayAsync()).Should().Equal(bytes);
    }
}
