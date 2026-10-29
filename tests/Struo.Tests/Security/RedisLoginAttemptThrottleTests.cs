using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AwesomeAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using StackExchange.Redis;
using Struo.Api.Auth;
using Struo.Application.Configuration;
using Struo.Application.Security;
using Struo.Tests.Support;
using Xunit;

namespace Struo.Tests.Security;

// Runs against a live Redis when STRUO_TEST_REDIS_CONNECTION is set and returns immediately
// otherwise, like the live database suites — judge a pass by its duration, not its colour.
// Keys live under a per-run prefix and are deleted afterwards.
public sealed class RedisLoginAttemptThrottleTests : IDisposable
{
    public const string EnvVar = "STRUO_TEST_REDIS_CONNECTION";

    private static readonly string? ConnectionString = Environment.GetEnvironmentVariable(EnvVar);

    private readonly string _prefix = $"struo-test-{Guid.NewGuid():N}:";
    private readonly List<string> _emails = [];

    private RedisLoginAttemptThrottle Throttle(int permitLimit = 10, int windowSeconds = 60) =>
        new(ConnectionString!, Options.Create(new LoginAccountRateLimitOptions
        {
            Enabled = true, PermitLimit = permitLimit, WindowSeconds = windowSeconds,
        }), _prefix);

    private string Email(string label)
    {
        var email = $"{label}-{Guid.NewGuid():N}@struo.test";
        _emails.Add(email);
        return email;
    }

    private string KeyOf(string email) =>
        _prefix + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(email.Trim().ToLowerInvariant())));

    public void Dispose()
    {
        if (ConnectionString is null) return;
        using var mux = ConnectionMultiplexer.Connect(ConnectionString);
        foreach (var email in _emails)
            mux.GetDatabase().KeyDelete(KeyOf(email));
    }

    [Fact]
    public async Task Parallel_attempts_cannot_exceed_limit()
    {
        if (ConnectionString is null) return;
        using var throttle = Throttle(permitLimit: 10);
        var email = Email("parallel");

        var states = await Task.WhenAll(Enumerable.Range(0, 50)
            .Select(_ => Task.Run(() => throttle.TryReserveAsync(email))));

        states.Count(s => !s.IsBlocked).Should().Be(10);
        states.Count(s => s.IsBlocked && s.RetryAfterSeconds >= 1).Should().Be(40);
    }

    [Fact]
    public async Task Successful_login_resets()
    {
        if (ConnectionString is null) return;
        using var throttle = Throttle(permitLimit: 10);
        var email = Email("reset");

        for (var i = 0; i < 3; i++)
            (await throttle.TryReserveAsync(email)).IsBlocked.Should().BeFalse();
        await throttle.ResetAsync(email);

        for (var i = 0; i < 10; i++)
            (await throttle.TryReserveAsync(email)).IsBlocked.Should().BeFalse();
        (await throttle.TryReserveAsync(email)).IsBlocked.Should().BeTrue();
    }

    [Fact]
    public async Task First_attempt_sets_the_window_ttl()
    {
        if (ConnectionString is null) return;
        using var throttle = Throttle(windowSeconds: 120);
        var email = Email("ttl");

        await throttle.TryReserveAsync(email);

        using var mux = await ConnectionMultiplexer.ConnectAsync(ConnectionString);
        var ttl = await mux.GetDatabase().KeyTimeToLiveAsync(KeyOf(email));
        ttl.Should().NotBeNull();
        ttl!.Value.TotalSeconds.Should().BeInRange(1, 120);
    }

    [Fact]
    public async Task Distributed_cache_entry_under_the_old_key_name_does_not_break_login()
    {
        if (ConnectionString is null) return;
        // RedisCache stores entries as a HASH under login-throttle:<hash>; INCR on that name would be WRONGTYPE.
        using var throttle = new RedisLoginAttemptThrottle(ConnectionString, Options.Create(
            new LoginAccountRateLimitOptions { Enabled = true, PermitLimit = 10, WindowSeconds = 60 }));
        var email = Email("old-key");
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(email)));
        using var mux = await ConnectionMultiplexer.ConnectAsync(ConnectionString);
        var db = mux.GetDatabase();
        await db.HashSetAsync("login-throttle:" + hash, "data", "x");
        try
        {
            (await throttle.TryReserveAsync(email)).IsBlocked.Should().BeFalse();
        }
        finally
        {
            await db.KeyDeleteAsync(["login-throttle:" + hash, "login-attempts:" + hash]);
        }
    }

    [Fact]
    public async Task Key_without_ttl_self_heals()
    {
        if (ConnectionString is null) return;
        using var throttle = Throttle(permitLimit: 10, windowSeconds: 120);
        var email = Email("no-ttl");
        using var mux = await ConnectionMultiplexer.ConnectAsync(ConnectionString);
        var db = mux.GetDatabase();
        await db.StringIncrementAsync(KeyOf(email));
        (await db.KeyTimeToLiveAsync(KeyOf(email))).Should().BeNull();

        await throttle.TryReserveAsync(email);

        (await db.KeyTimeToLiveAsync(KeyOf(email))).Should().NotBeNull();
    }
}

// A throttle that cannot reach its store must fail the login, never skip the check.
[Collection("ApiIntegration")]
public class UnreachableRedisThrottleTests(ApiFactory factory)
{
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

    [Fact]
    public async Task Unreachable_redis_fails_closed()
    {
        var hasher = new CountingPasswordHasher();
        var host = factory.WithWebHostBuilder(b =>
        {
            b.ConfigureAppConfiguration((_, c) => c.AddInMemoryCollection(new Dictionary<string, string?>
            {
                // Nothing listens on port 1.
                ["Redis:ConnectionString"] = "127.0.0.1:1,connectTimeout=1000,syncTimeout=1000",
                ["RateLimiting:Login:Enabled"] = "false",
            }));
            b.ConfigureServices(s =>
            {
                s.AddSingleton<IPasswordHasher>(hasher);
                DataProtectionTestHost.RemoveKeyRingLoader(s);
            });
        });

        var response = await host.CreateClient().PostAsJsonAsync(
            "/api/auth/login", new { email = $"redis-down-{Guid.NewGuid():N}@struo.local", password = "x" });

        ((int)response.StatusCode).Should().BeInRange(500, 599);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        doc.RootElement.GetProperty("error").GetProperty("code").GetString().Should().NotBeNullOrEmpty();
        hasher.VerifyCallCount.Should().Be(0);
    }
}
