// tests/Struo.Tests/Files/FileSoftDeleteEndpointTests.cs
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using AwesomeAssertions;
using Struo.Tests.Support;
using Xunit;

namespace Struo.Tests.Files;

// DELETE /api/files/{id} defaults to trash (not a hard delete); ?purge=true still hard
// deletes; POST /api/files/{id}/restore reverses a trash. Mirrors the WAF harness already used by
// Struo.Tests.Api.SoftDeleteEndpointTests (generic /items path) and Struo.Tests.Files.FileRbacTests
// (multipart upload + RBAC pattern for the dedicated files pipeline).
[Collection("ApiIntegration")]
public class FileSoftDeleteEndpointTests(ApiFactory factory)
{
    private readonly ApiFactory _factory = factory;
    private static JsonElement Root(string body) => JsonDocument.Parse(body).RootElement;

    private static MultipartFormDataContent Multipart(byte[] bytes, string name, string contentType)
    {
        var content = new ByteArrayContent(bytes);
        content.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        return new MultipartFormDataContent { { content, "file", name } };
    }

    private static async Task<string> UploadAsync(HttpClient client, string name = "trash-me.txt")
    {
        var resp = await client.PostAsync("/api/files", Multipart(Encoding.UTF8.GetBytes("x"), name, "text/plain"));
        resp.StatusCode.Should().Be(HttpStatusCode.Created);
        return Root(await resp.Content.ReadAsStringAsync()).GetProperty("data").GetProperty("id").GetString()!;
    }

    [Fact]
    public async Task Delete_defaults_to_trash_then_restore_reappears()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();
        var id = await UploadAsync(client);

        (await client.DeleteAsync($"/api/files/{id}")).StatusCode.Should().Be(HttpStatusCode.NoContent);

        // Trashed: filtered out of Get/Download — 404, not gone-forever.
        (await client.GetAsync($"/api/files/{id}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await client.GetAsync($"/api/files/{id}/content")).StatusCode.Should().Be(HttpStatusCode.NotFound);

        (await client.PostAsync($"/api/files/{id}/restore", null)).StatusCode.Should().Be(HttpStatusCode.NoContent);

        (await client.GetAsync($"/api/files/{id}")).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Purge_true_removes_a_trashed_file_permanently()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();
        var id = await UploadAsync(client, "purge-me.txt");

        (await client.DeleteAsync($"/api/files/{id}")).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await client.DeleteAsync($"/api/files/{id}?purge=true")).StatusCode.Should().Be(HttpStatusCode.NoContent);

        // Gone for good: restore no longer finds a row to restore.
        (await client.PostAsync($"/api/files/{id}/restore", null)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await client.GetAsync($"/api/files/{id}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Delete_unknown_id_is_404()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();
        (await client.DeleteAsync($"/api/files/{Guid.NewGuid()}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Restore_of_a_live_file_is_404()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();
        var id = await UploadAsync(client, "still-live.txt");
        (await client.PostAsync($"/api/files/{id}/restore", null)).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Restore_without_delete_permission_is_forbidden()
    {
        var (client, _) = await _factory.CreateEditorClientAsync(
            readCollections: ["file"], writeCollections: ["file"]); // read+write, no delete
        (await client.PostAsync($"/api/files/{Guid.NewGuid()}/restore", null)).StatusCode
            .Should().Be(HttpStatusCode.Forbidden);
    }
}
