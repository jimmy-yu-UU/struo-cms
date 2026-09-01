using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using AwesomeAssertions;
using Struo.Tests.Support;
using Xunit;

namespace Struo.Tests.Api;

/// <summary>
/// Pins that <c>X-Content-Type-Options: nosniff</c> is present on every kind of response this host
/// produces — a plain success, an error envelope, and a raw file stream — because the header is
/// registered first in the pipeline specifically so it survives every downstream short-circuit
/// (CORS preflight, the exception handler, a bare 404). See <c>docs/guide/en/15-deployment-operations-testing.md</c>'s
/// "Production checklist" for why this is the only header the application sends itself.
/// </summary>
[Collection("ApiIntegration")]
public class SecurityResponseHeadersTests(ApiFactory factory)
{
    private readonly ApiFactory _factory = factory;
    private const string HeaderName = "X-Content-Type-Options";
    private const string ExpectedValue = "nosniff";

    [Fact]
    public async Task Ok_json_response_carries_nosniff()
    {
        var client = _factory.CreateClient(); // /api/config is anonymous
        var resp = await client.GetAsync("/api/config");
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        resp.Headers.GetValues(HeaderName).Should().ContainSingle().Which.Should().Be(ExpectedValue);
    }

    [Fact]
    public async Task Unauthorized_error_envelope_carries_nosniff()
    {
        var client = _factory.CreateClient(); // anonymous, no session cookie
        var resp = await client.GetAsync("/api/auth/me");
        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        resp.Headers.GetValues(HeaderName).Should().ContainSingle().Which.Should().Be(ExpectedValue);
    }

    [Fact]
    public async Task NotFound_error_envelope_carries_nosniff()
    {
        var client = _factory.CreateClient();
        var resp = await client.GetAsync("/api/items/nope");
        resp.StatusCode.Should().Be(HttpStatusCode.NotFound);
        resp.Headers.GetValues(HeaderName).Should().ContainSingle().Which.Should().Be(ExpectedValue);
    }

    [Fact]
    public async Task File_download_response_carries_nosniff()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();
        var upload = new ByteArrayContent(Encoding.UTF8.GetBytes("payload"));
        upload.Headers.ContentType = new MediaTypeHeaderValue("text/plain");
        var mp = new MultipartFormDataContent { { upload, "file", "f.txt" } };
        var uploadResp = await client.PostAsync("/api/files", mp);
        var id = JsonDocument.Parse(await uploadResp.Content.ReadAsStringAsync())
            .RootElement.GetProperty("data").GetProperty("id").GetString();

        var resp = await client.GetAsync($"/api/files/{id}/content");
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        resp.Headers.GetValues(HeaderName).Should().ContainSingle().Which.Should().Be(ExpectedValue);
    }
}
