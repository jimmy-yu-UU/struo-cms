using AwesomeAssertions;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using Struo.Application.Configuration;
using Struo.Application.Security;
using Struo.Infrastructure.Identity;
using Xunit;

namespace Struo.Tests.Identity;

// Unit-level coverage of DistributedCacheLoginAttemptThrottle against a real MemoryDistributedCache
// (not a mock) — this class's whole job is a read/write contract against IDistributedCache, so a mock
// would only assert what the mock was told to return. tests/Struo.Tests/Api/LoginAccountThrottleTests
// covers the same mechanism wired through AuthController/the real HTTP pipeline instead.
public class DistributedCacheLoginAttemptThrottleTests
{
    private static IDistributedCache MemoryCache() =>
        new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions()));

    private static DistributedCacheLoginAttemptThrottle Throttle(
        IDistributedCache cache, int permitLimit = 3, int windowSeconds = 60, bool enabled = true) =>
        new(cache, Options.Create(new LoginAccountRateLimitOptions
        {
            Enabled = enabled, PermitLimit = permitLimit, WindowSeconds = windowSeconds,
        }));

    [Fact]
    public async Task CheckAsync_is_not_blocked_before_any_failure_is_recorded()
    {
        var throttle = Throttle(MemoryCache());
        var state = await throttle.CheckAsync("nobody@struo.test");
        state.IsBlocked.Should().BeFalse();
    }

    [Fact]
    public async Task CheckAsync_blocks_once_failures_reach_the_permit_limit()
    {
        var cache = MemoryCache();
        var throttle = Throttle(cache, permitLimit: 3);
        const string email = "spammed@struo.test";

        for (var i = 0; i < 3; i++)
            await throttle.RecordFailureAsync(email);

        var state = await throttle.CheckAsync(email);
        state.IsBlocked.Should().BeTrue();
        state.RetryAfterSeconds.Should().BeGreaterThanOrEqualTo(1);
    }

    [Fact]
    public async Task CheckAsync_does_not_block_one_failure_short_of_the_limit()
    {
        var cache = MemoryCache();
        var throttle = Throttle(cache, permitLimit: 3);
        const string email = "almost@struo.test";

        await throttle.RecordFailureAsync(email);
        await throttle.RecordFailureAsync(email);

        (await throttle.CheckAsync(email)).IsBlocked.Should().BeFalse();
    }

    [Fact]
    public async Task ResetAsync_clears_a_blocked_account()
    {
        var cache = MemoryCache();
        var throttle = Throttle(cache, permitLimit: 1);
        const string email = "reset-me@struo.test";

        await throttle.RecordFailureAsync(email);
        (await throttle.CheckAsync(email)).IsBlocked.Should().BeTrue();

        await throttle.ResetAsync(email);

        (await throttle.CheckAsync(email)).IsBlocked.Should().BeFalse();
    }

    [Fact]
    public async Task Throttling_is_per_account()
    {
        var cache = MemoryCache();
        var throttle = Throttle(cache, permitLimit: 1);

        await throttle.RecordFailureAsync("account-a@struo.test");

        (await throttle.CheckAsync("account-a@struo.test")).IsBlocked.Should().BeTrue();
        (await throttle.CheckAsync("account-b@struo.test")).IsBlocked.Should().BeFalse();
    }

    [Fact]
    public async Task Email_matching_is_case_and_whitespace_insensitive()
    {
        var cache = MemoryCache();
        var throttle = Throttle(cache, permitLimit: 1);

        await throttle.RecordFailureAsync("  Spray@Struo.Test  ");

        (await throttle.CheckAsync("spray@struo.test")).IsBlocked.Should().BeTrue();
    }

    [Fact]
    public async Task Disabled_never_blocks_regardless_of_failure_count()
    {
        var cache = MemoryCache();
        var throttle = Throttle(cache, permitLimit: 1, enabled: false);
        const string email = "off@struo.test";

        await throttle.RecordFailureAsync(email);
        await throttle.RecordFailureAsync(email);
        await throttle.RecordFailureAsync(email);

        (await throttle.CheckAsync(email)).IsBlocked.Should().BeFalse();
    }

    [Fact]
    public async Task Repeated_failures_do_not_slide_the_window_forward()
    {
        // permitLimit=1 means the account is already over budget from the FIRST failure onward, for
        // as long as its window stays open — which is what makes this discriminate fixed-vs-sliding:
        // if a SECOND failure, recorded while still inside the original window, incorrectly restarted
        // the window's clock, blocking would still be in effect at t≈3.2s (1.5s past the second
        // failure's own would-be 3s window). Observing NOT-blocked at that point instead proves the
        // window's expiry stayed anchored to the FIRST failure's timestamp the whole time.
        var cache = MemoryCache();
        var throttle = Throttle(cache, permitLimit: 1, windowSeconds: 3);
        const string email = "fixed-window@struo.test";

        await throttle.RecordFailureAsync(email); // count=1, window starts at t=0, ends at t=3s
        await Task.Delay(1500); // t≈1.5s — still inside the original window
        await throttle.RecordFailureAsync(email); // count=2, SAME window if fixed; a fresh one (ending ≈4.5s) if sliding

        await Task.Delay(1700); // t≈3.2s: past the ORIGINAL window's end, short of a slid one's
        (await throttle.CheckAsync(email)).IsBlocked.Should().BeFalse(
            "the window must have expired at its original 3s mark, not been pushed out to ~4.5s by " +
            "the second failure");
    }

    [Fact]
    public async Task RetryAfterSeconds_decreases_as_the_window_elapses()
    {
        var cache = MemoryCache();
        var throttle = Throttle(cache, permitLimit: 1, windowSeconds: 3);
        const string email = "decreasing@struo.test";

        await throttle.RecordFailureAsync(email);
        var first = await throttle.CheckAsync(email);
        first.IsBlocked.Should().BeTrue();

        await Task.Delay(1500);
        var second = await throttle.CheckAsync(email);
        second.IsBlocked.Should().BeTrue();

        second.RetryAfterSeconds.Should().BeLessThan(first.RetryAfterSeconds);
    }
}
