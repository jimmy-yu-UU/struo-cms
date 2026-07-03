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

    private async Task<string> Upload(System.Net.Http.HttpClient c, string body = "data")
    {
        var content = new ByteArrayContent(Encoding.UTF8.GetBytes(body));
        content.Headers.ContentType = new MediaTypeHeaderValue("text/plain");
        var mp = new MultipartFormDataContent { { content, "file", "f.txt" } };
        var resp = await c.PostAsync("/api/files", mp);
        return Root(await resp.Content.ReadAsStringAsync()).GetProperty("data").GetProperty("id").GetString()!;
    }

    private static Task<System.Net.Http.HttpResponseMessage> SetStatus(
        System.Net.Http.HttpClient c, string id, string status) =>
        c.PutAsJsonAsync($"/api/items/file/{id}", new
        {
            status,
            translations = new Dictionary<string, object> { ["en"] = new { title = "T", alt = (string?)null } }
        });

    private static Task<System.Net.Http.HttpResponseMessage> Publish(System.Net.Http.HttpClient c, string id) =>
        SetStatus(c, id, "published");

    [Fact]
    public async Task Upload_defaults_to_published()
    {
        // CHANGE 1: dimensions are extracted synchronously during upload, so there is no pending
        // async step that "draft" was gating — new uploads must be immediately usable/servable.
        var c = await _factory.CreateAuthenticatedClientAsync();
        var content = new ByteArrayContent(Encoding.UTF8.GetBytes("data"));
        content.Headers.ContentType = new MediaTypeHeaderValue("text/plain");
        var mp = new MultipartFormDataContent { { content, "file", "f.txt" } };
        var resp = await c.PostAsync("/api/files", mp);
        Root(await resp.Content.ReadAsStringAsync()).GetProperty("data").GetProperty("status").GetString()
            .Should().Be("published");
    }

    [Fact]
    public async Task Nonpublished_file_is_404_to_anonymous_but_visible_to_authenticated()
    {
        // CHANGE 2: authenticated callers (cookie or bearer) may fetch/serve a file of any status;
        // anonymous callers remain restricted to published-only.
        var c = await _factory.CreateAuthenticatedClientAsync();
        var id = await Upload(c, "payload");
        var setDraft = await SetStatus(c, id, "draft");
        setDraft.StatusCode.Should().Be(HttpStatusCode.OK, await setDraft.Content.ReadAsStringAsync());

        var anon = _factory.CreateClient();
        (await anon.GetAsync($"/api/files/{id}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await anon.GetAsync($"/api/files/{id}/content")).StatusCode.Should().Be(HttpStatusCode.NotFound);

        var infoResp = await c.GetAsync($"/api/files/{id}");
        infoResp.StatusCode.Should().Be(HttpStatusCode.OK);
        Root(await infoResp.Content.ReadAsStringAsync()).GetProperty("data").GetProperty("status").GetString()
            .Should().Be("draft");

        var contentResp = await c.GetAsync($"/api/files/{id}/content");
        contentResp.StatusCode.Should().Be(HttpStatusCode.OK);
        (await contentResp.Content.ReadAsStringAsync()).Should().Be("payload");
    }

    [Fact]
    public async Task Nonpublished_file_is_visible_to_bearer_authenticated_caller()
    {
        // CHANGE 2 explicitly covers "cookie OR bearer" — prove the bearer half independently
        // of the cookie-based admin client used elsewhere in this file.
        var c = await _factory.CreateAuthenticatedClientAsync();
        var id = await Upload(c, "payload");
        (await SetStatus(c, id, "draft")).StatusCode.Should().Be(HttpStatusCode.OK);

        var gen = await c.PostAsync($"/api/users/{_factory.AdminUserId}/access-token", null);
        gen.StatusCode.Should().Be(HttpStatusCode.OK);
        var token = Root(await gen.Content.ReadAsStringAsync()).GetProperty("data").GetProperty("token").GetString()!;

        var bearer = _factory.CreateClient();
        bearer.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        (await bearer.GetAsync($"/api/files/{id}")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await bearer.GetAsync($"/api/files/{id}/content")).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Published_download_streams_bytes()
    {
        var c = await _factory.CreateAuthenticatedClientAsync();
        var id = await Upload(c, "payload");
        await Publish(c, id);
        var resp = await c.GetAsync($"/api/files/{id}/content");
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        (await resp.Content.ReadAsStringAsync()).Should().Be("payload");
    }

    [Fact]
    public async Task Published_info_returns_metadata()
    {
        var c = await _factory.CreateAuthenticatedClientAsync();
        var id = await Upload(c);
        var pub = await Publish(c, id);
        pub.StatusCode.Should().Be(HttpStatusCode.OK, await pub.Content.ReadAsStringAsync());
        var resp = await c.GetAsync($"/api/files/{id}");
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        Root(await resp.Content.ReadAsStringAsync()).GetProperty("data").GetProperty("status").GetString()
            .Should().Be("published");
    }

    [Fact]
    public async Task Published_file_remains_reachable_anonymously()
    {
        // Regression: published files stay publicly servable (e.g. public image serving).
        var c = await _factory.CreateAuthenticatedClientAsync();
        var id = await Upload(c, "payload");
        await Publish(c, id);

        var anon = _factory.CreateClient();
        (await anon.GetAsync($"/api/files/{id}")).StatusCode.Should().Be(HttpStatusCode.OK);
        var contentResp = await anon.GetAsync($"/api/files/{id}/content");
        contentResp.StatusCode.Should().Be(HttpStatusCode.OK);
        (await contentResp.Content.ReadAsStringAsync()).Should().Be("payload");
    }

    [Fact]
    public async Task Delete_removes_file_then_download_404()
    {
        var c = await _factory.CreateAuthenticatedClientAsync();
        var id = await Upload(c);
        await Publish(c, id);
        (await c.DeleteAsync($"/api/files/{id}")).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await c.GetAsync($"/api/files/{id}/content")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
