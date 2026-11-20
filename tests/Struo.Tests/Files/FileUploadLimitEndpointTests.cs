// tests/Struo.Tests/Files/FileUploadLimitEndpointTests.cs
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using AwesomeAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Struo.Api.Auth;
using Struo.Tests.Support;
using Xunit;

namespace Struo.Tests.Files;

/// <summary>
/// An upload over <c>Struo:Files:MaxUploadBytes</c> answers 413 PAYLOAD_TOO_LARGE with the limit in
/// <c>error.details[0].maxBytes</c>; an upload within it still succeeds.
/// </summary>
[Collection("ApiIntegration")]
public class FileUploadLimitEndpointTests(ApiFactory factory)
{
    private const int Limit = 64;

    private WebApplicationFactory<Program> Host() =>
        factory.WithWebHostBuilder(b => b.ConfigureAppConfiguration((_, c) =>
            c.AddInMemoryCollection(new Dictionary<string, string?> { ["Struo:Files:MaxUploadBytes"] = Limit.ToString() })));

    private async Task<HttpClient> AdminClientAsync(WebApplicationFactory<Program> host)
    {
        await factory.EnsureAdminSeededAsync();
        var client = host.CreateClient();
        client.DefaultRequestHeaders.Add(CsrfProtectionMiddleware.HeaderName, "1");
        (await client.PostAsJsonAsync("/api/auth/login",
            new { email = ApiFactory.AdminEmail, password = ApiFactory.AdminPassword })).EnsureSuccessStatusCode();
        return client;
    }

    private static MultipartFormDataContent Multipart(int size)
    {
        var content = new ByteArrayContent(new byte[size]);
        content.Headers.ContentType = new MediaTypeHeaderValue("text/plain");
        return new MultipartFormDataContent { { content, "file", "note.txt" } };
    }

    [Fact]
    public async Task An_upload_over_the_limit_is_413_PAYLOAD_TOO_LARGE_with_the_limit_in_details()
    {
        using var host = Host();
        var client = await AdminClientAsync(host);

        var resp = await client.PostAsync("/api/files", Multipart(Limit + 1));

        var body = await resp.Content.ReadAsStringAsync();
        resp.StatusCode.Should().Be(HttpStatusCode.RequestEntityTooLarge, body);
        var error = JsonDocument.Parse(body).RootElement.GetProperty("error");
        error.GetProperty("code").GetString().Should().Be("PAYLOAD_TOO_LARGE");
        error.GetProperty("message").GetString().Should().Be($"File exceeds the maximum size of {Limit} bytes.");
        var detail = error.GetProperty("details").EnumerateArray().Single();
        detail.GetProperty("field").GetString().Should().Be("file");
        detail.GetProperty("maxBytes").GetInt64().Should().Be(Limit);
    }

    [Fact]
    public async Task An_upload_exactly_at_the_limit_succeeds()
    {
        using var host = Host();
        var client = await AdminClientAsync(host);

        var resp = await client.PostAsync("/api/files", Multipart(Limit));

        resp.StatusCode.Should().Be(HttpStatusCode.Created, await resp.Content.ReadAsStringAsync());
    }
}
