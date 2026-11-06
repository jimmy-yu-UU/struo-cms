using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using AwesomeAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Serilog.Core;
using Serilog.Events;
using Struo.Api.Auth;
using Struo.Application.Security;
using Struo.Tests.Support;
using Xunit;

namespace Struo.Tests.Api;

/// <summary>
/// Every error envelope carries <c>error.traceId</c>, and it is the id the request log records for the
/// same request. One test per writer of an error envelope.
/// </summary>
[Collection("ApiIntegration")]
public class TraceIdEnvelopeTests(ApiFactory factory)
{
    private readonly ApiFactory _factory = factory;
    private readonly CapturingLogSink _sink = new();

    private sealed class ThrowingCredentialStore : IUserCredentialStore
    {
        public Task<UserCredential?> FindByEmailAsync(string email, CancellationToken ct = default) =>
            throw new InvalidOperationException("credential store unreachable");

        public Task<UserCredential?> FindByAccessTokenAsync(string tokenHash, CancellationToken ct = default) =>
            throw new InvalidOperationException("credential store unreachable");

        public Task<UserCredential?> FindByIdAsync(Guid id, CancellationToken ct = default) =>
            throw new InvalidOperationException("credential store unreachable");

        public Task TouchAccessTokenLastUsedAsync(Guid userId, DateTime nowUtc, CancellationToken ct = default) =>
            throw new InvalidOperationException("credential store unreachable");
    }

    private WebApplicationFactory<Program> Host(
        Action<IServiceCollection>? services = null, Dictionary<string, string?>? config = null) =>
        _factory.WithWebHostBuilder(b =>
        {
            if (config is not null)
                b.ConfigureAppConfiguration((_, c) => c.AddInMemoryCollection(config));
            b.ConfigureServices(s =>
            {
                s.AddSingleton<ILogEventSink>(_sink);
                services?.Invoke(s);
            });
        });

    private async Task<string> AssertEnvelopeCarriesLoggedTraceAsync(HttpResponseMessage response, HttpStatusCode status)
    {
        response.StatusCode.Should().Be(status);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var traceId = doc.RootElement.GetProperty("error").GetProperty("traceId").GetString();
        traceId.Should().MatchRegex("^[0-9a-f]{32}$");
        var logged = await _sink.WaitForRequestLogAsync(traceId!);
        logged.Should().NotBeNull("the request log must record the id the response carries");
        logged!.Properties["StatusCode"].ToString().Should().Be(((int)status).ToString());
        return traceId!;
    }

    private async Task<HttpClient> AdminClientAsync(WebApplicationFactory<Program> host)
    {
        await _factory.EnsureAdminSeededAsync();
        var client = host.CreateClient();
        client.DefaultRequestHeaders.Add(CsrfProtectionMiddleware.HeaderName, "1");
        (await client.PostAsJsonAsync("/api/auth/login",
            new { email = ApiFactory.AdminEmail, password = ApiFactory.AdminPassword })).EnsureSuccessStatusCode();
        return client;
    }

    [Fact]
    public async Task Masked_500_carries_the_trace_id_and_the_error_log_entry_has_it_too()
    {
        using var host = Host(s => s.AddScoped<IUserCredentialStore, ThrowingCredentialStore>());
        var client = host.CreateClient();
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/ping");
        request.Headers.Add("Authorization", "Bearer any-token-value");

        var traceId = await AssertEnvelopeCarriesLoggedTraceAsync(
            await client.SendAsync(request), HttpStatusCode.InternalServerError);

        _sink.Events.Should().Contain(e =>
            e.Level == LogEventLevel.Error
            && e.MessageTemplate.Text == "Unhandled API exception"
            && e.TraceId!.Value.ToHexString() == traceId);
    }

    [Fact]
    public async Task Domain_exception_404_carries_the_trace_id()
    {
        using var host = Host();
        var client = host.CreateClient();
        await AssertEnvelopeCarriesLoggedTraceAsync(await client.GetAsync("/api/items/nope"), HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Domain_exception_400_carries_the_trace_id()
    {
        using var host = Host();
        var client = host.CreateClient();
        await AssertEnvelopeCarriesLoggedTraceAsync(
            await client.GetAsync("/api/items/fxArticle?filter[ghost][_eq]=x"), HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Bare_not_found_result_carries_the_trace_id()
    {
        using var host = Host();
        var client = host.CreateClient();
        await AssertEnvelopeCarriesLoggedTraceAsync(
            await client.GetAsync($"/api/items/fxArticle/{Guid.NewGuid()}"), HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Fail_helper_result_carries_the_trace_id()
    {
        using var host = Host();
        var client = host.CreateClient();
        client.DefaultRequestHeaders.Add(CsrfProtectionMiddleware.HeaderName, "1");
        var resp = await client.PostAsJsonAsync("/api/auth/login", new { email = "nope@x.test", password = "wrongwrong" });
        await AssertEnvelopeCarriesLoggedTraceAsync(resp, HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Model_validation_400_carries_the_trace_id()
    {
        using var host = Host();
        var client = await AdminClientAsync(host);

        var resp = await client.PostAsync("/api/items/fxArticle",
            new StringContent("{ not json", Encoding.UTF8, "application/json"));

        await AssertEnvelopeCarriesLoggedTraceAsync(resp, HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Csrf_403_carries_the_trace_id()
    {
        using var host = Host();
        var client = await AdminClientAsync(host);
        client.DefaultRequestHeaders.Remove(CsrfProtectionMiddleware.HeaderName);

        var resp = await client.PutAsJsonAsync("/api/settings/branding", new { brandName = "X", logoFileId = (string?)null });

        await AssertEnvelopeCarriesLoggedTraceAsync(resp, HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Authentication_challenge_401_carries_the_trace_id()
    {
        using var host = Host();
        var client = host.CreateClient();
        await AssertEnvelopeCarriesLoggedTraceAsync(await client.GetAsync("/api/auth/me"), HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Rate_limit_429_carries_the_trace_id()
    {
        using var host = Host(config: new Dictionary<string, string?>
        {
            ["RateLimiting:Login:Enabled"] = "true",
            ["RateLimiting:Login:PermitLimit"] = "1",
            ["RateLimiting:Login:WindowSeconds"] = "60",
        });
        var client = host.CreateClient();
        client.DefaultRequestHeaders.Add(CsrfProtectionMiddleware.HeaderName, "1");
        var body = new { email = "nobody@struo.local", password = "wrong-password" };
        await client.PostAsJsonAsync("/api/auth/login", body);

        var limited = await client.PostAsJsonAsync("/api/auth/login", body);

        await AssertEnvelopeCarriesLoggedTraceAsync(limited, HttpStatusCode.TooManyRequests);
    }
}
