using System.Net;
using System.Net.Http.Json;
using AwesomeAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Struo.Api.Auth;
using Struo.Application.Security;
using Struo.Infrastructure.Identity;
using Struo.Tests.Support;
using Xunit;

namespace Struo.Tests.Security;

// The per-account throttle counts an attempt BEFORE the password is verified, so parallel guesses
// against one account cannot slip past the limit by all reading the same count.
[Collection("ApiIntegration")]
public class LoginThrottleConcurrencyTests(ApiFactory factory)
{
    private static string FreshEmail(string label) => $"{label}-{Guid.NewGuid():N}@struo.local";

    // Counts verifications; delegates to the real hasher so seeded accounts can still sign in.
    private sealed class CountingPasswordHasher : IPasswordHasher
    {
        private readonly Argon2idPasswordHasher _inner = new();
        public int VerifyCallCount;
        public string Hash(string password) => _inner.Hash(password);
        public bool Verify(string encoded, string password)
        {
            Interlocked.Increment(ref VerifyCallCount);
            return _inner.Verify(encoded, password);
        }
    }

    private WebApplicationFactory<Program> Host(int permitLimit, IPasswordHasher hasher) =>
        factory.WithWebHostBuilder(b =>
        {
            b.ConfigureAppConfiguration((_, c) => c.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["RateLimiting:LoginAccount:Enabled"] = "true",
                ["RateLimiting:LoginAccount:PermitLimit"] = permitLimit.ToString(),
                ["RateLimiting:LoginAccount:WindowSeconds"] = "300",
                ["RateLimiting:Login:Enabled"] = "false",
                ["RateLimiting:LoginConcurrency:Enabled"] = "false",
            }));
            b.ConfigureServices(s => s.AddSingleton(hasher));
        });

    private static Task<HttpResponseMessage> LoginAsync(HttpClient client, string email, string password) =>
        client.PostAsJsonAsync("/api/auth/login", new { email, password });

    [Fact]
    public async Task Parallel_wrong_passwords_cannot_exceed_limit()
    {
        const int limit = 10;
        var hasher = new CountingPasswordHasher();
        var client = Host(limit, hasher).CreateClient();
        var email = FreshEmail("parallel");

        var responses = await Task.WhenAll(Enumerable.Range(0, 50)
            .Select(_ => Task.Run(() => LoginAsync(client, email, "wrong-password"))));

        hasher.VerifyCallCount.Should().BeLessThanOrEqualTo(limit);
        responses.Count(r => r.StatusCode == HttpStatusCode.Unauthorized).Should().Be(limit);
        responses.Count(r => r.StatusCode == (HttpStatusCode)429).Should().Be(50 - limit);
    }

    [Fact]
    public async Task Successful_login_resets()
    {
        const int limit = 10;
        var hasher = new CountingPasswordHasher();
        var host = Host(limit, hasher);
        var (email, password, _) = await factory.SeedEditorAsync([], []);
        var client = host.CreateClient();
        client.DefaultRequestHeaders.Add(CsrfProtectionMiddleware.HeaderName, "1");

        for (var i = 0; i < 3; i++)
            (await LoginAsync(client, email, "wrong-password")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await LoginAsync(client, email, password)).StatusCode.Should().Be(HttpStatusCode.OK);

        for (var i = 0; i < limit; i++)
            (await LoginAsync(client, email, "wrong-password")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await LoginAsync(client, email, "wrong-password")).StatusCode.Should().Be((HttpStatusCode)429);
    }
}
