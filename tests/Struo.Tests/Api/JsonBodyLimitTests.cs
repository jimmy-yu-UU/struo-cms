using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using AwesomeAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Struo.Api.Auth;
using Struo.Application.Configuration;
using Struo.Tests.Support;
using Xunit;

namespace Struo.Tests.Api;

/// <summary>
/// <c>Api:MaxJsonBodyBytes</c> caps JSON request bodies (REST JSON endpoints and /graphql) with a
/// 413 PAYLOAD_TOO_LARGE envelope; multipart uploads keep their own cap.
/// </summary>
[Collection("ApiIntegration")]
public class JsonBodyLimitTests(ApiFactory factory)
{
    private const int Limit = 1024;
    private readonly ApiFactory _factory = factory;

    private WebApplicationFactory<Program> Host(string limit = "1024") =>
        _factory.WithWebHostBuilder(b => b.ConfigureAppConfiguration((_, c) =>
            c.AddInMemoryCollection(new Dictionary<string, string?> { ["Api:MaxJsonBodyBytes"] = limit })));

    private async Task<HttpClient> AdminClientAsync(WebApplicationFactory<Program> host)
    {
        await _factory.EnsureAdminSeededAsync();
        var client = host.CreateClient();
        client.DefaultRequestHeaders.Add(CsrfProtectionMiddleware.HeaderName, "1");
        (await client.PostAsJsonAsync("/api/auth/login",
            new { email = ApiFactory.AdminEmail, password = ApiFactory.AdminPassword })).EnsureSuccessStatusCode();
        return client;
    }

    private static StringContent Json(int approxBytes) =>
        new("{\"title\":\"" + new string('a', approxBytes) + "\"}", Encoding.UTF8, "application/json");

    private sealed class UnknownLengthContent(byte[] bytes) : HttpContent
    {
        protected override Task SerializeToStreamAsync(Stream stream, System.Net.TransportContext? context) =>
            stream.WriteAsync(bytes, 0, bytes.Length);

        protected override bool TryComputeLength(out long length)
        {
            length = 0;
            return false;
        }

        public static UnknownLengthContent Of(int size, string mediaType = "application/json")
        {
            var content = new UnknownLengthContent(
                Encoding.UTF8.GetBytes("{\"title\":\"" + new string('a', size) + "\"}"));
            content.Headers.ContentType = new MediaTypeHeaderValue(mediaType);
            return content;
        }
    }

    private static async Task AssertPayloadTooLargeEnvelopeAsync(HttpResponseMessage response)
    {
        response.StatusCode.Should().Be(HttpStatusCode.RequestEntityTooLarge, await response.Content.ReadAsStringAsync());
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        doc.RootElement.GetProperty("success").GetBoolean().Should().BeFalse();
        var error = doc.RootElement.GetProperty("error");
        error.GetProperty("code").GetString().Should().Be("PAYLOAD_TOO_LARGE");
        error.GetProperty("traceId").GetString().Should().NotBeNullOrEmpty();
    }

    // /graphql keeps its own error shape: { errors: [{ message, extensions: { code, traceId } }] }.
    private static async Task AssertGraphQlPayloadTooLargeAsync(HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.RequestEntityTooLarge, body);
        using var doc = JsonDocument.Parse(body);
        var extensions = doc.RootElement.GetProperty("errors")[0].GetProperty("extensions");
        extensions.GetProperty("code").GetString().Should().Be("PAYLOAD_TOO_LARGE");
        extensions.GetProperty("traceId").GetString().Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void Default_limit_is_four_megabytes()
    {
        _factory.Services.GetRequiredService<IOptions<ApiOptions>>().Value.MaxJsonBodyBytes
            .Should().Be(4 * 1024 * 1024);
    }

    [Fact]
    public async Task Items_create_under_the_limit_is_not_rejected()
    {
        using var host = Host();
        var client = await AdminClientAsync(host);
        var resp = await client.PostAsync("/api/items/fxArticle", Json(Limit - 200));
        resp.StatusCode.Should().NotBe(HttpStatusCode.RequestEntityTooLarge);
    }

    [Fact]
    public async Task Items_create_over_the_limit_by_content_length_is_413()
    {
        using var host = Host();
        var client = await AdminClientAsync(host);
        await AssertPayloadTooLargeEnvelopeAsync(await client.PostAsync("/api/items/fxArticle", Json(Limit + 200)));
    }

    [Fact]
    public async Task Another_json_endpoint_over_the_limit_is_413()
    {
        using var host = Host();
        var client = host.CreateClient();
        client.DefaultRequestHeaders.Add(CsrfProtectionMiddleware.HeaderName, "1");
        var resp = await client.PostAsJsonAsync("/api/auth/login",
            new { email = new string('a', Limit * 2) + "@x.test", password = "wrongwrong" });
        await AssertPayloadTooLargeEnvelopeAsync(resp);
    }

    [Fact]
    public async Task Chunked_body_without_content_length_over_the_limit_is_413()
    {
        using var host = Host();
        var client = await AdminClientAsync(host);
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/items/fxArticle")
        {
            Content = UnknownLengthContent.Of(Limit * 3),
        };
        request.Headers.TransferEncodingChunked = true;
        request.Content.Headers.ContentLength.Should().BeNull();

        await AssertPayloadTooLargeEnvelopeAsync(await client.SendAsync(request));
    }

    [Fact]
    public async Task Chunked_body_under_the_limit_is_not_rejected()
    {
        using var host = Host();
        var client = await AdminClientAsync(host);
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/items/fxArticle")
        {
            Content = UnknownLengthContent.Of(Limit / 2),
        };
        request.Headers.TransferEncodingChunked = true;

        (await client.SendAsync(request)).StatusCode.Should().NotBe(HttpStatusCode.RequestEntityTooLarge);
    }

    [Fact]
    public async Task Graphql_under_the_limit_is_served()
    {
        using var host = Host();
        var resp = await host.CreateClient().PostAsJsonAsync("/graphql", new { query = "{ __typename }" });
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Graphql_over_the_limit_is_413_with_a_graphql_error()
    {
        var query = "{ __typename } # " + new string('a', Limit * 2);
        using var host = Host();
        var resp = await host.CreateClient().PostAsJsonAsync("/graphql", new { query });
        await AssertGraphQlPayloadTooLargeAsync(resp);
    }

    [Fact]
    public async Task Graphql_chunked_over_the_limit_is_413_with_a_graphql_error()
    {
        var content = new StringContent(
            JsonSerializer.Serialize(new { query = "{ __typename } # " + new string('a', Limit * 3) }),
            Encoding.UTF8, "application/json");
        var request = new HttpRequestMessage(HttpMethod.Post, "/graphql")
        {
            Content = new UnknownLengthContent(await content.ReadAsByteArrayAsync()),
        };
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        request.Headers.TransferEncodingChunked = true;

        using var host = Host();
        await AssertGraphQlPayloadTooLargeAsync(await host.CreateClient().SendAsync(request));
    }

    [Fact]
    public async Task Multipart_upload_above_the_json_limit_still_works()
    {
        using var host = Host();
        var client = await AdminClientAsync(host);
        var file = new ByteArrayContent(Encoding.UTF8.GetBytes(new string('x', Limit * 4)));
        file.Headers.ContentType = new MediaTypeHeaderValue("text/plain");
        var content = new MultipartFormDataContent { { file, "file", "big.txt" } };

        var resp = await client.PostAsync("/api/files", content);

        resp.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    [SqliteOnlyTheory("starts a full host on a SQLite temp database")]
    [InlineData("0")]
    [InlineData("-5")]
    [InlineData("abc")]
    public async Task A_limit_that_is_not_positive_fails_startup_naming_the_key(string value)
    {
        var error = await ForkHost.StartupErrorAsync(mutate: s => s["Api:MaxJsonBodyBytes"] = value);

        Exception? failure = ExceptionChainSearch.FindInner<OptionsValidationException>(error);
        failure ??= ExceptionChainSearch.FindInner<InvalidOperationException>(error);
        failure.Should().NotBeNull(error?.ToString());
        failure!.Message.Should().Contain("Api:MaxJsonBodyBytes");
    }
}
