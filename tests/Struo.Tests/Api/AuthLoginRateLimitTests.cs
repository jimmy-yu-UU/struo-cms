// tests/Struo.Tests/Api/AuthLoginRateLimitTests.cs
using System.Collections.Generic;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AwesomeAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Struo.Tests.Support;
using Xunit;

namespace Struo.Tests.Api;

// SEC-7: POST /api/auth/login is brute-forceable and every anonymous attempt burns full
// Argon2id CPU. Verifies the app-layer, login-only, fixed-window-per-client-IP limiter added in
// Program.cs (Microsoft.AspNetCore.RateLimiting). Each test spins up its OWN derived host via
// WithWebHostBuilder (isolated TestServer + in-memory limiter state) with a small configured
// PermitLimit, so this cannot interfere with the base ApiFactory's login-heavy fixture used by
// the other ~55 integration test classes in the shared "ApiIntegration" collection.
[Collection("ApiIntegration")]
public class AuthLoginRateLimitTests(ApiFactory factory)
{
    private readonly ApiFactory _factory = factory;

    private static async Task<HttpResponseMessage> LoginAsync(HttpClient client) =>
        await client.PostAsJsonAsync("/api/auth/login",
            new { email = "nobody@struo.local", password = "wrong-password" });

    [Fact]
    public async Task Login_beyond_permit_limit_returns_429_with_error_envelope()
    {
        var f = _factory.WithWebHostBuilder(b =>
            b.ConfigureAppConfiguration((_, c) => c.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["RateLimiting:Login:PermitLimit"] = "2",
                ["RateLimiting:Login:WindowSeconds"] = "60",
            })));
        var client = f.CreateClient();

        var first = await LoginAsync(client);
        var second = await LoginAsync(client);
        var third = await LoginAsync(client);

        first.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        second.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        third.StatusCode.Should().Be((HttpStatusCode)429);

        using var doc = JsonDocument.Parse(await third.Content.ReadAsStringAsync());
        doc.RootElement.GetProperty("success").GetBoolean().Should().BeFalse();
        doc.RootElement.GetProperty("error").GetProperty("code").GetString().Should().Be("TOO_MANY_REQUESTS");
    }

    [Fact]
    public async Task Login_beyond_permit_limit_sets_retry_after_header()
    {
        var f = _factory.WithWebHostBuilder(b =>
            b.ConfigureAppConfiguration((_, c) => c.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["RateLimiting:Login:PermitLimit"] = "1",
                ["RateLimiting:Login:WindowSeconds"] = "60",
            })));
        var client = f.CreateClient();

        await LoginAsync(client);
        var rejected = await LoginAsync(client);

        rejected.StatusCode.Should().Be((HttpStatusCode)429);
        rejected.Headers.RetryAfter.Should().NotBeNull();
    }

    [Fact]
    public async Task Logout_and_me_are_not_rate_limited_by_the_login_policy()
    {
        // A tiny PermitLimit on the "login" policy must not affect other auth endpoints —
        // EnableRateLimiting("login") is applied to the Login action only.
        var f = _factory.WithWebHostBuilder(b =>
            b.ConfigureAppConfiguration((_, c) => c.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["RateLimiting:Login:PermitLimit"] = "1",
                ["RateLimiting:Login:WindowSeconds"] = "60",
            })));

        // Exhaust the login policy's budget for this host's partition.
        await LoginAsync(f.CreateClient());
        var exhausted = await LoginAsync(f.CreateClient());
        exhausted.StatusCode.Should().Be((HttpStatusCode)429);

        // /api/auth/me is unauthenticated here (no session) so it should 401, never 429.
        var meResp = await f.CreateClient().GetAsync("/api/auth/me");
        meResp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        // SEC-7: this test's namesake claim — /api/auth/logout is also unaffected by the exhausted
        // "login" policy partition. No session exists here either, but the point is the SAME:
        // repeated calls must never surface 429 (the login limiter is scoped to the Login action).
        var logoutClient = f.CreateClient();
        for (var i = 0; i < 3; i++)
        {
            var logoutResp = await logoutClient.PostAsync("/api/auth/logout", content: null);
            logoutResp.StatusCode.Should().NotBe((HttpStatusCode)429);
        }
    }
}
