// tests/Struo.Tests/Api/LoginAccountThrottleTests.cs
using System.Collections.Generic;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AwesomeAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Struo.Api.Auth;
using Struo.Application.Security;
using Struo.Tests.Support;
using Xunit;

namespace Struo.Tests.Api;

// Exercises AuthController.Login's second, independent login defense: the per-ACCOUNT throttle
// (ILoginAttemptThrottle / DistributedCacheLoginAttemptThrottle), which counts failures by the email
// in the request body rather than by client IP — the layer that ships ENABLED by default (see
// AuthLoginRateLimitTests for the per-client-IP limiter, which ships disabled). Each test spins up its
// OWN derived host via WithWebHostBuilder (isolated TestServer), so this cannot interfere with the base
// ApiFactory's login-heavy fixture used by the other ~55 integration test classes in the shared
// "ApiIntegration" collection — that fixture already pins a generously high
// RateLimiting:LoginAccount:PermitLimit for exactly that reason (ApiFactory.cs). Unlike the per-client-IP
// limiter's in-memory state, ILoginAttemptThrottle's IDistributedCache backend is Redis in this
// repository's own local dev/test configuration (Testing/appsettings.Development.json), which — unlike a
// fresh-per-host in-memory cache — PERSISTS ACROSS separate `dotnet test` invocations. Every test below
// therefore uses a fresh Guid-suffixed email rather than a fixed literal, so a leftover counter from an
// earlier run (or an earlier test in this same run) can never make a test spuriously see itself already
// throttled.
[Collection("ApiIntegration")]
public class LoginAccountThrottleTests(ApiFactory factory)
{
    private readonly ApiFactory _factory = factory;

    private static string FreshEmail(string label) => $"{label}-{Guid.NewGuid():N}@struo.local";

    // Deliberately fast: real Argon2id work would make this suite slow without adding coverage — the
    // one test that cares about hashing COST (Blocked_requests_never_reach_the_password_hasher) counts
    // calls, it does not need real hashing.
    private sealed class CountingPasswordHasher : IPasswordHasher
    {
        public int VerifyCallCount;
        public string Hash(string password) => "fake-hash";
        public bool Verify(string encoded, string password)
        {
            Interlocked.Increment(ref VerifyCallCount);
            return false;
        }
    }

    private WebApplicationFactory<Program> Host(
        int permitLimit = 3, int windowSeconds = 300, bool enabled = true, IPasswordHasher? hasher = null) =>
        _factory.WithWebHostBuilder(b =>
        {
            b.ConfigureAppConfiguration((_, c) => c.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["RateLimiting:LoginAccount:Enabled"] = enabled ? "true" : "false",
                ["RateLimiting:LoginAccount:PermitLimit"] = permitLimit.ToString(),
                ["RateLimiting:LoginAccount:WindowSeconds"] = windowSeconds.ToString(),
                // Isolate from the per-client-IP limiter entirely — this file is about the per-account
                // throttle only.
                ["RateLimiting:Login:Enabled"] = "false",
            }));
            if (hasher is not null)
                b.ConfigureServices(services => services.AddSingleton<IPasswordHasher>(hasher));
        });

    private static Task<HttpResponseMessage> LoginAsync(HttpClient client, string email, string password) =>
        client.PostAsJsonAsync("/api/auth/login", new { email, password });

    private static async Task<(string? code, string? message)> ErrorOf(HttpResponseMessage resp)
    {
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
        var error = doc.RootElement.GetProperty("error");
        return (error.GetProperty("code").GetString(), error.GetProperty("message").GetString());
    }

    [Fact]
    public async Task Failed_logins_past_permit_limit_return_429_with_envelope_and_retry_after()
    {
        var f = Host(permitLimit: 3);
        var client = f.CreateClient();
        var email = FreshEmail("spammed");

        for (var i = 0; i < 3; i++)
        {
            var resp = await LoginAsync(client, email, "wrong-password");
            resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        }

        var blocked = await LoginAsync(client, email, "wrong-password");

        blocked.StatusCode.Should().Be((HttpStatusCode)429);
        blocked.Headers.RetryAfter.Should().NotBeNull();
        var (code, message) = await ErrorOf(blocked);
        code.Should().Be("TOO_MANY_REQUESTS");
        // Same wording the per-client-IP limiter's OnRejected callback writes (Program.cs) — the two
        // layers are deliberately indistinguishable to a client.
        message.Should().Be("Too many login attempts. Please try again later.");
    }

    [Fact]
    public async Task Nonexistent_email_and_wrong_password_produce_identical_shape_even_once_throttled()
    {
        var f = Host(permitLimit: 2);
        var (realUserEmail, _, _) = await _factory.SeedEditorAsync([], []);
        var unknownEmail = FreshEmail("nobody");

        var unknownClient = f.CreateClient();
        var wrongPasswordClient = f.CreateClient();

        // Same unknown email reused every iteration (rather than a fresh one) — the throttle is
        // per-account, so a fresh email each time would never accumulate failures and this could
        // never observe the throttled (429) step at all. Step through every attempt in lockstep: both
        // must 401 with the identical body up to the permit limit, and both must 429 with the
        // identical body once throttled — at every step.
        for (var i = 0; i < 3; i++)
        {
            var unknown = await LoginAsync(unknownClient, unknownEmail, "whatever");
            var wrongPassword = await LoginAsync(wrongPasswordClient, realUserEmail, "definitely-wrong");

            unknown.StatusCode.Should().Be(wrongPassword.StatusCode);
            var (unknownCode, unknownMessage) = await ErrorOf(unknown);
            var (wrongCode, wrongMessage) = await ErrorOf(wrongPassword);
            unknownCode.Should().Be(wrongCode);
            unknownMessage.Should().Be(wrongMessage);

            if (i < 2)
            {
                unknown.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
            }
            else
            {
                unknown.StatusCode.Should().Be((HttpStatusCode)429);
                unknown.Headers.RetryAfter.Should().NotBeNull();
                wrongPassword.Headers.RetryAfter.Should().NotBeNull();
            }
        }
    }

    [Fact]
    public async Task A_successful_login_resets_the_counter()
    {
        var f = Host(permitLimit: 3);
        var (email, password, _) = await _factory.SeedEditorAsync([], []);
        var client = f.CreateClient();
        // Once the login below succeeds, this client carries a session cookie, and
        // CsrfProtectionMiddleware then requires the CSRF header on every further mutation from it —
        // including this same login endpoint on the next (post-reset) attempt.
        client.DefaultRequestHeaders.Add(CsrfProtectionMiddleware.HeaderName, "1");

        // Fail PermitLimit - 1 times: one short of tripping the throttle.
        for (var i = 0; i < 2; i++)
            (await LoginAsync(client, email, "wrong-password")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        var success = await LoginAsync(client, email, password);
        success.StatusCode.Should().Be(HttpStatusCode.OK);

        // Fail again, immediately: had the counter not been reset, this single failure would already
        // be sitting on top of the 2 pre-existing ones and would trip the limit at permitLimit=3.
        var afterReset = await LoginAsync(client, email, "wrong-password");
        afterReset.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Throttling_is_per_account_not_shared_across_accounts()
    {
        var f = Host(permitLimit: 1);
        var client = f.CreateClient();
        var accountA = FreshEmail("account-a");
        var accountB = FreshEmail("account-b");

        var firstA = await LoginAsync(client, accountA, "wrong-password");
        firstA.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        var exhaustedA = await LoginAsync(client, accountA, "wrong-password");
        exhaustedA.StatusCode.Should().Be((HttpStatusCode)429);

        // A different account, from the SAME client, must be untouched.
        var firstB = await LoginAsync(client, accountB, "wrong-password");
        firstB.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Disabled_throttle_never_returns_429_even_beyond_permit_limit()
    {
        var f = Host(permitLimit: 1, enabled: false);
        var client = f.CreateClient();
        var email = FreshEmail("unthrottled");

        for (var i = 0; i < 5; i++)
        {
            var resp = await LoginAsync(client, email, "wrong-password");
            resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        }
    }

    [Fact]
    public async Task Blocked_requests_never_reach_the_password_hasher()
    {
        var hasher = new CountingPasswordHasher();
        var f = Host(permitLimit: 2, hasher: hasher);
        var client = f.CreateClient();
        var email = FreshEmail("spy-target");

        await LoginAsync(client, email, "wrong-password");
        await LoginAsync(client, email, "wrong-password");
        hasher.VerifyCallCount.Should().Be(2, "both attempts were within the permit limit and must have reached AuthService");

        var blocked = await LoginAsync(client, email, "wrong-password");
        blocked.StatusCode.Should().Be((HttpStatusCode)429);
        hasher.VerifyCallCount.Should().Be(2, "a throttled request must be rejected BEFORE AuthenticateAsync " +
            "runs, so it must spend no password-verification cost at all");
    }
}
