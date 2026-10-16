using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AwesomeAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Struo.Application.Security;
using Struo.Infrastructure.Identity;
using Struo.Tests.Support;
using Xunit;

namespace Struo.Tests.Api;

// RateLimiting:LoginConcurrency caps how many logins verify a password at once, so a flood of
// parallel attempts cannot pin every core on Argon2id.
[Collection("ApiIntegration")]
public class LoginConcurrencyLimitTests(ApiFactory factory)
{
    // Holds every verification until released; delegates to the real hasher.
    private sealed class GatedPasswordHasher : IPasswordHasher
    {
        private readonly Argon2idPasswordHasher _inner = new();
        private readonly ManualResetEventSlim _gate = new(false);
        public int Entered;
        public void Release() => _gate.Set();
        public string Hash(string password) => _inner.Hash(password);
        public bool Verify(string encoded, string password)
        {
            Interlocked.Increment(ref Entered);
            _gate.Wait(TimeSpan.FromSeconds(30));
            return _inner.Verify(encoded, password);
        }
    }

    private WebApplicationFactory<Program> Host(GatedPasswordHasher hasher, int permitLimit, int queueLimit) =>
        factory.WithWebHostBuilder(b =>
        {
            b.ConfigureAppConfiguration((_, c) => c.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["RateLimiting:Login:Enabled"] = "false",
                ["RateLimiting:LoginConcurrency:Enabled"] = "true",
                ["RateLimiting:LoginConcurrency:PermitLimit"] = permitLimit.ToString(),
                ["RateLimiting:LoginConcurrency:QueueLimit"] = queueLimit.ToString(),
            }));
            b.ConfigureServices(s => s.AddSingleton<IPasswordHasher>(hasher));
        });

    [Fact]
    public async Task Global_concurrency_cap()
    {
        var hasher = new GatedPasswordHasher();
        var host = Host(hasher, permitLimit: 1, queueLimit: 0);
        var (email, password, _) = await factory.SeedEditorAsync([], []);
        var first = Task.Run(() => host.CreateClient().PostAsJsonAsync("/api/auth/login", new { email, password }));
        await WaitUntilAsync(() => hasher.Entered == 1);

        var second = await host.CreateClient()
            .PostAsJsonAsync("/api/auth/login", new { email, password = "other" });

        second.StatusCode.Should().Be((HttpStatusCode)429);
        second.Headers.RetryAfter.Should().NotBeNull();
        using var doc = JsonDocument.Parse(await second.Content.ReadAsStringAsync());
        var error = doc.RootElement.GetProperty("error");
        error.GetProperty("code").GetString().Should().Be("TOO_MANY_REQUESTS");
        error.GetProperty("message").GetString().Should().Be("Too many login attempts. Please try again later.");
        hasher.Entered.Should().Be(1);

        hasher.Release();
        (await first).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Disabled_cap_lets_parallel_logins_through()
    {
        var hasher = new GatedPasswordHasher();
        var host = factory.WithWebHostBuilder(b =>
        {
            b.ConfigureAppConfiguration((_, c) => c.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["RateLimiting:Login:Enabled"] = "false",
                ["RateLimiting:LoginConcurrency:Enabled"] = "false",
                ["RateLimiting:LoginConcurrency:PermitLimit"] = "1",
                ["RateLimiting:LoginConcurrency:QueueLimit"] = "0",
            }));
            b.ConfigureServices(s => s.AddSingleton<IPasswordHasher>(hasher));
        });
        var (email, password, _) = await factory.SeedEditorAsync([], []);

        var clients = Enumerable.Range(0, 3).Select(_ => host.CreateClient()).ToArray();
        var logins = clients
            .Select(client => Task.Run(() => client.PostAsJsonAsync("/api/auth/login", new { email, password })))
            .ToArray();
        await WaitUntilAsync(() => hasher.Entered == 3);
        hasher.Release();

        foreach (var login in logins)
            (await login).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(20);
        while (!condition())
        {
            if (DateTime.UtcNow > deadline) throw new TimeoutException("Condition not reached.");
            await Task.Delay(20);
        }
    }
}
