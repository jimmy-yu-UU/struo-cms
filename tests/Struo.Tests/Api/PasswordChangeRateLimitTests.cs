// tests/Struo.Tests/Api/PasswordChangeRateLimitTests.cs
using System.Collections.Generic;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AwesomeAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Struo.Api.Auth;
using Struo.Tests.Support;
using Xunit;

namespace Struo.Tests.Api;

// The self-service path runs a full Argon2id verify on a caller-supplied CurrentPassword, and it sits
// BEHIND authentication — so the anonymous "login" limiter never sees it. Partitioned by the
// authenticated user id (not client IP): the caller can only spend budget on the one account whose
// session they hold, and the partition is immune to the reverse-proxy caveat that makes the login
// limiter's per-IP key collapse to a single bucket.
//
// NOTE: WithWebHostBuilder returns a WebApplicationFactory<Program> (an internal
// DelegatedWebApplicationFactory under the hood, NOT an ApiFactory) — casting it back to ApiFactory
// compiles but throws at runtime. Both hosts share the ONE SqliteTestDatabase owned by the base
// ApiFactory (its connection string is layered into every derived host's configuration), so tests
// here seed users through the base factory's ApiFactory.SeedEditorAsync and then log in against the
// DERIVED host's own HttpClient/cookie jar.
[Collection("ApiIntegration")]
public class PasswordChangeRateLimitTests(ApiFactory factory)
{
    private readonly ApiFactory _factory = factory;

    private WebApplicationFactory<Program> Host(string permitLimit, string enabled = "true") =>
        _factory.WithWebHostBuilder(b =>
            b.ConfigureAppConfiguration((_, c) => c.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["RateLimiting:Password:Enabled"] = enabled,
                ["RateLimiting:Password:PermitLimit"] = permitLimit,
                ["RateLimiting:Password:WindowSeconds"] = "60",
            })));

    private async Task<(HttpClient client, Guid userId)> CreateEditorClientOnAsync(
        WebApplicationFactory<Program> host, string[] readCollections, string[] writeCollections)
    {
        var (email, password, userId) = await _factory.SeedEditorAsync(readCollections, writeCollections);
        var client = host.CreateClient();
        // The SPA sends the CSRF header on every cookie-authenticated mutation; mirror that here so
        // this cookie-based client isn't rejected by CsrfProtectionMiddleware.
        client.DefaultRequestHeaders.Add(CsrfProtectionMiddleware.HeaderName, "1");
        var resp = await client.PostAsJsonAsync("/api/auth/login", new { email, password });
        resp.EnsureSuccessStatusCode();
        return (client, userId);
    }

    private async Task<HttpClient> CreateAuthenticatedClientOnAsync(WebApplicationFactory<Program> host)
    {
        // Ensures the shared admin row exists in the DB both hosts point at (idempotent); the
        // returned client from the BASE factory is unused here — only the seeding side effect matters.
        await _factory.CreateAuthenticatedClientAsync();

        var client = host.CreateClient();
        client.DefaultRequestHeaders.Add(CsrfProtectionMiddleware.HeaderName, "1");
        var resp = await client.PostAsJsonAsync("/api/auth/login",
            new { email = ApiFactory.AdminEmail, password = ApiFactory.AdminPassword });
        resp.EnsureSuccessStatusCode();
        return client;
    }

    private static Task<HttpResponseMessage> AttemptAsync(HttpClient client, Guid userId) =>
        client.PutAsJsonAsync($"/api/users/{userId}/password",
            new { newPassword = "new-password-123", currentPassword = "WRONG" });

    [Fact]
    public async Task Attempts_beyond_the_permit_limit_return_429_with_the_error_envelope()
    {
        var host = Host(permitLimit: "2");
        var (client, userId) = await CreateEditorClientOnAsync(host, [], []);

        (await AttemptAsync(client, userId)).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await AttemptAsync(client, userId)).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var third = await AttemptAsync(client, userId);

        third.StatusCode.Should().Be((HttpStatusCode)429);
        third.Headers.RetryAfter.Should().NotBeNull();

        using var doc = JsonDocument.Parse(await third.Content.ReadAsStringAsync());
        doc.RootElement.GetProperty("success").GetBoolean().Should().BeFalse();
        doc.RootElement.GetProperty("error").GetProperty("code").GetString().Should().Be("TOO_MANY_REQUESTS");
        // The message must not talk about logins — this is the change-password endpoint.
        doc.RootElement.GetProperty("error").GetProperty("message").GetString()
            .Should().NotContain("login");
    }

    // The core assertion for the partition key. If the key were the client IP (fixed for TestServer)
    // both users would share one bucket and the second user's first attempt would already be a 429.
    [Fact]
    public async Task Two_different_users_do_not_share_a_bucket()
    {
        var host = Host(permitLimit: "1");
        var (clientA, userA) = await CreateEditorClientOnAsync(host, [], []);
        var (clientB, userB) = await CreateEditorClientOnAsync(host, [], []);

        (await AttemptAsync(clientA, userA)).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await AttemptAsync(clientA, userA)).StatusCode.Should().Be((HttpStatusCode)429);

        // A's exhausted budget must not touch B.
        (await AttemptAsync(clientB, userB)).StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task With_the_limiter_disabled_no_attempt_is_ever_429()
    {
        var host = Host(permitLimit: "1", enabled: "false");
        var (client, userId) = await CreateEditorClientOnAsync(host, [], []);

        for (var i = 0; i < 4; i++)
            (await AttemptAsync(client, userId)).StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task The_password_policy_does_not_limit_other_endpoints()
    {
        var host = Host(permitLimit: "1");
        var client = await CreateAuthenticatedClientOnAsync(host);

        for (var i = 0; i < 4; i++)
            (await client.GetAsync("/api/auth/me")).StatusCode.Should().NotBe((HttpStatusCode)429);
    }
}
