using System.Net;
using System.Net.Http.Headers;
using System.Text;
using AwesomeAssertions;
using Struo.Tests.Support;
using Xunit;

namespace Struo.Tests.Files;

// H4: the Files API (upload/delete) must enforce the same per-collection RBAC as every other
// collection. Being authenticated is not enough — the caller needs write/delete on the "file"
// collection. Otherwise any logged-in user (incl. a role-less SSO user) could upload or delete
// arbitrary media.
[Collection("ApiIntegration")]
public class FileRbacTests(ApiFactory factory)
{
    private readonly ApiFactory _factory = factory;

    private static MultipartFormDataContent Multipart(byte[] bytes, string name, string contentType)
    {
        var content = new ByteArrayContent(bytes);
        content.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        return new MultipartFormDataContent { { content, "file", name } };
    }

    [Fact]
    public async Task Roleless_user_upload_is_forbidden()
    {
        var (client, _) = await _factory.CreateRolelessClientAsync();
        var resp = await client.PostAsync("/api/files",
            Multipart(Encoding.UTF8.GetBytes("x"), "n.txt", "text/plain"));
        resp.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Roleless_user_delete_is_forbidden()
    {
        var (client, _) = await _factory.CreateRolelessClientAsync();
        var resp = await client.DeleteAsync($"/api/files/{Guid.NewGuid()}");
        resp.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Editor_without_file_write_upload_is_forbidden()
    {
        // Write grant on "article" only — no "file" write.
        var (client, _) = await _factory.CreateEditorClientAsync(
            readCollections: ["article"], writeCollections: ["article"]);
        var resp = await client.PostAsync("/api/files",
            Multipart(Encoding.UTF8.GetBytes("x"), "n.txt", "text/plain"));
        resp.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Editor_with_file_write_can_upload()
    {
        var (client, _) = await _factory.CreateEditorClientAsync(
            readCollections: ["file"], writeCollections: ["file"]);
        var resp = await client.PostAsync("/api/files",
            Multipart(Encoding.UTF8.GetBytes("hello"), "ok.txt", "text/plain"));
        resp.StatusCode.Should().Be(HttpStatusCode.Created);
    }
}
