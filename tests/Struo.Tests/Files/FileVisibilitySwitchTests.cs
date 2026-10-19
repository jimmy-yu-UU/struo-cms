using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Struo.Application.Files;
using Struo.Tests.Support;
using Xunit;

namespace Struo.Tests.Files;

// A published file is readable by anyone holding its id; a draft only by callers with a "file"
// write grant. Editors switch a file between the two through the generic items endpoint, and the
// upload default comes from Struo:Files:DefaultStatus.
[Collection("ApiIntegration")]
public class FileVisibilitySwitchTests(ApiFactory factory)
{
    private readonly ApiFactory _factory = factory;
    private static JsonElement Root(string b) => JsonDocument.Parse(b).RootElement;

    private static async Task<string> UploadAsync(HttpClient client)
    {
        var content = new ByteArrayContent(Encoding.UTF8.GetBytes("bytes"));
        content.Headers.ContentType = new MediaTypeHeaderValue("text/plain");
        var up = await client.PostAsync("/api/files", new MultipartFormDataContent { { content, "file", "v.txt" } });
        up.StatusCode.Should().Be(HttpStatusCode.Created, await up.Content.ReadAsStringAsync());
        return Root(await up.Content.ReadAsStringAsync()).GetProperty("data").GetProperty("id").GetString()!;
    }

    private static Task<HttpResponseMessage> SetStatusAsync(HttpClient client, string id, string status) =>
        client.PutAsJsonAsync($"/api/items/file/{id}", new { status });

    [Fact]
    public async Task Upload_with_draft_default_is_hidden_from_anonymous_and_visible_to_a_writer()
    {
        var options = _factory.Services.GetRequiredService<FileStorageOptions>();
        var admin = await _factory.CreateAuthenticatedClientAsync();
        string id;
        options.DefaultStatus = "draft";
        try { id = await UploadAsync(admin); }
        finally { options.DefaultStatus = "published"; }

        (await _factory.CreateClient().GetAsync($"/api/files/{id}/content")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await admin.GetAsync($"/api/files/{id}/content")).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Upload_with_default_configuration_is_published()
    {
        var admin = await _factory.CreateAuthenticatedClientAsync();
        var id = await UploadAsync(admin);
        (await _factory.CreateClient().GetAsync($"/api/files/{id}/content")).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Writer_switches_a_file_between_draft_and_published()
    {
        var admin = await _factory.CreateAuthenticatedClientAsync();
        var anon = _factory.CreateClient();
        var id = await UploadAsync(admin);

        var toDraft = await SetStatusAsync(admin, id, "draft");
        toDraft.StatusCode.Should().Be(HttpStatusCode.OK, await toDraft.Content.ReadAsStringAsync());
        (await anon.GetAsync($"/api/files/{id}/content")).StatusCode.Should().Be(HttpStatusCode.NotFound);

        var toPublished = await SetStatusAsync(admin, id, "published");
        toPublished.StatusCode.Should().Be(HttpStatusCode.OK, await toPublished.Content.ReadAsStringAsync());
        (await anon.GetAsync($"/api/files/{id}/content")).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Invalid_status_is_rejected_with_400()
    {
        var admin = await _factory.CreateAuthenticatedClientAsync();
        var id = await UploadAsync(admin);
        (await SetStatusAsync(admin, id, "archived")).StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Status_change_without_file_write_is_denied()
    {
        var admin = await _factory.CreateAuthenticatedClientAsync();
        var id = await UploadAsync(admin);
        var (reader, _) = await _factory.CreateEditorClientAsync(readCollections: ["file"], writeCollections: ["article"]);

        var resp = await SetStatusAsync(reader, id, "draft");
        ((int)resp.StatusCode).Should().BeOneOf(403, 404);
        (await _factory.CreateClient().GetAsync($"/api/files/{id}/content")).StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
