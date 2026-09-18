using System.Net;
using System.Net.Http.Headers;
using System.Text;
using AwesomeAssertions;
using Struo.Tests.Support;
using Xunit;

namespace Struo.Tests.Files;

// The shipped default upload content-type whitelist (appsettings.json
// Struo:Files:AllowedContentTypes) must reject an unexpected type. An empty list allows every
// content type through (see FileService.UploadAsync's Length > 0 guard); the shipped default is
// deliberately non-empty. A whitelisted type still uploads.
[Collection("ApiIntegration")]
public class FileUploadWhitelistTests(ApiFactory factory)
{
    private readonly ApiFactory _factory = factory;

    private static MultipartFormDataContent Multipart(byte[] bytes, string name, string contentType)
    {
        var content = new ByteArrayContent(bytes);
        content.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        return new MultipartFormDataContent { { content, "file", name } };
    }

    [Fact]
    public async Task Upload_of_non_whitelisted_type_is_rejected()
    {
        var c = await _factory.CreateAuthenticatedClientAsync();
        var resp = await c.PostAsync("/api/files",
            Multipart([1, 2, 3], "x.bin", "application/octet-stream"));
        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest, await resp.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Upload_of_whitelisted_type_succeeds()
    {
        var c = await _factory.CreateAuthenticatedClientAsync();
        var resp = await c.PostAsync("/api/files",
            Multipart(Encoding.UTF8.GetBytes("ok"), "ok.txt", "text/plain"));
        resp.StatusCode.Should().Be(HttpStatusCode.Created, await resp.Content.ReadAsStringAsync());
    }
}
