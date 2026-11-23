using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using AwesomeAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Serilog.Core;
using Serilog.Events;
using Struo.Api.Auth;
using Struo.Application.Files;
using Struo.Tests.Api;
using Struo.Tests.Support;
using Xunit;

namespace Struo.Tests.Files;

/// <summary>
/// The request body of <c>POST /api/files</c>, through the whole pipeline: what is read before the caller
/// is authorized, which size cap the endpoint gets, and what an unreadable form answers.
/// </summary>
[Collection("ApiIntegration")]
public class FileUploadRequestBodyEndpointTests(ApiFactory factory)
{
    private const int Limit = 64;
    private const string UnreadableForm = "The request form could not be read.";

    /// <summary>Replaces the body of every POST to /api/files with a counting stream and records the
    /// request-size feature each request ends up with.</summary>
    private sealed class Probe
    {
        public CountingBodyStream? Body;
        public readonly Dictionary<string, RecordingLimitFeature> Features = new();
    }

    private sealed class ProbeFilter(Probe probe) : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            app.Use(async (context, nextMiddleware) =>
            {
                var feature = new RecordingLimitFeature();
                context.Features.Set<IHttpMaxRequestBodySizeFeature>(feature);
                probe.Features[context.Request.Path.Value!] = feature;
                if (HttpMethods.IsPost(context.Request.Method) && context.Request.Path.StartsWithSegments("/api/files"))
                {
                    probe.Body = new CountingBodyStream(context.Request.Body);
                    context.Request.Body = probe.Body;
                }

                await nextMiddleware();
            });
            next(app);
        };
    }

    /// <summary>Gives every POST to /api/files a body whose read fails with the given exception.</summary>
    private sealed class FailingBodyFilter(Exception failure) : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            app.Use(async (context, nextMiddleware) =>
            {
                if (HttpMethods.IsPost(context.Request.Method) && context.Request.Path.StartsWithSegments("/api/files"))
                    context.Request.Body = new FailingBodyStream(failure);
                await nextMiddleware();
            });
            next(app);
        };
    }

    private WebApplicationFactory<Program> Host(Probe? probe = null) =>
        factory.WithWebHostBuilder(b =>
        {
            b.ConfigureAppConfiguration((_, c) => c.AddInMemoryCollection(
                new Dictionary<string, string?> { ["Struo:Files:MaxUploadBytes"] = Limit.ToString() }));
            if (probe is not null)
                b.ConfigureServices(services => services.AddSingleton<IStartupFilter>(new ProbeFilter(probe)));
        });

    private static HttpClient Client(WebApplicationFactory<Program> host)
    {
        var client = host.CreateClient();
        client.DefaultRequestHeaders.Add(CsrfProtectionMiddleware.HeaderName, "1");
        return client;
    }

    private static async Task<HttpClient> LoginAsync(WebApplicationFactory<Program> host, string email, string password)
    {
        var client = Client(host);
        (await client.PostAsJsonAsync("/api/auth/login", new { email, password })).EnsureSuccessStatusCode();
        return client;
    }

    private async Task<HttpClient> AdminAsync(WebApplicationFactory<Program> host)
    {
        await factory.EnsureAdminSeededAsync();
        return await LoginAsync(host, ApiFactory.AdminEmail, ApiFactory.AdminPassword);
    }

    private static MultipartFormDataContent Multipart(int size)
    {
        var content = new ByteArrayContent(new byte[size]);
        content.Headers.ContentType = new MediaTypeHeaderValue("text/plain");
        return new MultipartFormDataContent { { content, "file", "note.txt" } };
    }

    private static async Task AssertBadFormAsync(HttpResponseMessage resp)
    {
        var body = await resp.Content.ReadAsStringAsync();
        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest, body);
        var error = JsonDocument.Parse(body).RootElement.GetProperty("error");
        error.GetProperty("code").GetString().Should().Be("BAD_USER_INPUT");
        error.GetProperty("message").GetString().Should().Be(UnreadableForm);
    }

    [Fact]
    public async Task An_unauthenticated_upload_is_refused_without_reading_its_body()
    {
        var probe = new Probe();
        using var host = Host(probe);
        var client = Client(host);

        var resp = await client.PostAsync("/api/files", Multipart(8192));

        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        probe.Body!.Bytes.Should().Be(0, "the form is read only after authorization");
    }

    [Fact]
    public async Task A_caller_without_the_file_write_grant_is_refused_without_reading_the_body()
    {
        var probe = new Probe();
        using var host = Host(probe);
        var (email, password, _) = await factory.SeedEditorAsync(["fxArticle"], ["fxArticle"]);
        var client = await LoginAsync(host, email, password);
        probe.Body = null;

        var resp = await client.PostAsync("/api/files", Multipart(8192));

        resp.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        JsonDocument.Parse(await resp.Content.ReadAsStringAsync()).RootElement.GetProperty("error")
            .GetProperty("message").GetString().Should().Be("Write not permitted.");
        probe.Body!.Bytes.Should().Be(0, "the write grant is checked before the form is read");
    }

    [Fact]
    public async Task An_authenticated_upload_reads_its_body()
    {
        var probe = new Probe();
        using var host = Host(probe);
        var client = await AdminAsync(host);

        var resp = await client.PostAsync("/api/files", Multipart(8192));
        var body = await resp.Content.ReadAsStringAsync();

        resp.StatusCode.Should().Be(HttpStatusCode.RequestEntityTooLarge, body);
        probe.Body!.Bytes.Should().BeGreaterThan(8192);
    }

    [Fact]
    public async Task The_upload_endpoint_gets_the_upload_limit_plus_headroom_and_other_endpoints_keep_the_default()
    {
        var probe = new Probe();
        using var host = Host(probe);
        var client = await AdminAsync(host);

        await client.PostAsync("/api/files", Multipart(8));
        var other = new StringContent("a=b", Encoding.ASCII, "application/x-www-form-urlencoded");
        await client.PostAsync("/api/auth/login", other);

        probe.Features["/api/files"].MaxRequestBodySize.Should().Be(Limit + FileStorageOptions.UploadFramingHeadroomBytes);
        probe.Features["/api/auth/login"].MaxRequestBodySize.Should().Be(30_000_000);
    }

    [Fact]
    public async Task A_multipart_request_without_a_boundary_is_a_400()
    {
        using var host = Host();
        var client = await AdminAsync(host);
        var content = new ByteArrayContent("hello"u8.ToArray());
        content.Headers.ContentType = new MediaTypeHeaderValue("multipart/form-data");

        await AssertBadFormAsync(await client.PostAsync("/api/files", content));
    }

    [Fact]
    public async Task A_truncated_multipart_body_is_a_400()
    {
        using var host = Host();
        var client = await AdminAsync(host);
        var content = new ByteArrayContent(Encoding.ASCII.GetBytes(
            "--xyz\r\nContent-Disposition: form-data; name=\"file\"; filename=\"a.txt\"\r\nContent-Type: text/plain\r\n\r\nabc"));
        content.Headers.ContentType = MediaTypeHeaderValue.Parse("multipart/form-data; boundary=xyz");

        await AssertBadFormAsync(await client.PostAsync("/api/files", content));
    }

    [Fact]
    public async Task More_form_values_than_the_form_limit_is_a_400()
    {
        using var host = Host();
        var client = await AdminAsync(host);
        var content = new MultipartFormDataContent();
        for (var i = 0; i < 1100; i++) content.Add(new StringContent("v"), $"f{i}");

        await AssertBadFormAsync(await client.PostAsync("/api/files", content));
    }

    [Fact]
    public async Task A_form_read_that_times_out_is_a_408_with_BAD_USER_INPUT_and_no_error_log()
    {
        var sink = new CapturingLogSink();
        var timeout = new BadHttpRequestException("Request body timeout.", StatusCodes.Status408RequestTimeout);
        using var host = Host().WithWebHostBuilder(b => b.ConfigureServices(services =>
        {
            services.AddSingleton<IStartupFilter>(new FailingBodyFilter(timeout));
            services.AddSingleton<ILogEventSink>(sink);
        }));
        var client = await AdminAsync(host);

        var resp = await client.PostAsync("/api/files", Multipart(8));
        var body = await resp.Content.ReadAsStringAsync();

        resp.StatusCode.Should().Be(HttpStatusCode.RequestTimeout, body);
        var error = JsonDocument.Parse(body).RootElement.GetProperty("error");
        error.GetProperty("code").GetString().Should().Be("BAD_USER_INPUT");
        error.GetProperty("message").GetString().Should().Be("The request body could not be read.");
        sink.Events.Should().NotContain(e => e.Level >= LogEventLevel.Error);
    }
}
