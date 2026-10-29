using AwesomeAssertions;
using Struo.Tests.Support;
using Xunit;

namespace Struo.Tests.Files;

[Collection("ApiIntegration")]
public class FileReferenceTests(ApiFactory factory)
{
    private readonly ApiFactory _factory = factory;

    [Fact]
    public async Task File_upload_and_info_round_trip()
    {
        // Smoke-test that the files subsystem is wired up in the integration host.
        var c = await _factory.CreateAuthenticatedClientAsync();
        var content = new ByteArrayContent([1, 2, 3]);
        content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("text/plain"); // whitelisted
        var mp = new MultipartFormDataContent { { content, "file", "smoke.txt" } };
        var resp = await c.PostAsync("/api/files", mp);
        resp.StatusCode.Should().Be(System.Net.HttpStatusCode.Created);
        var body = System.Text.Json.JsonDocument.Parse(await resp.Content.ReadAsStringAsync()).RootElement;
        body.GetProperty("data").GetProperty("id").GetString().Should().NotBeNullOrEmpty();
    }
}
