using AwesomeAssertions;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using Struo.Application.Configuration;
using Struo.Application.Security;
using Struo.Infrastructure.Identity;
using Struo.Tests.Support;
using Xunit;

namespace Struo.Tests.Identity;

// Unit-level coverage of DistributedCacheLoginAttemptThrottle against a real MemoryDistributedCache
// (not a mock) — this class's whole job is a read/write contract against IDistributedCache, so a mock
// would only assert what the mock was told to return. tests/Struo.Tests/Api/LoginAccountThrottleTests
// covers the same mechanism wired through AuthController/the real HTTP pipeline instead.
public class DistributedCacheLoginAttemptThrottleTests
{
    private static MemoryDistributedCache MemoryCache() =>
        new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions()));

    private static DistributedCacheLoginAttemptThrottle Throttle(
        IDistributedCache cache, int permitLimit = 3, int windowSeconds = 60, bool enabled = true,
        TimeProvider? clock = null) =>
        new(cache, Options.Create(new LoginAccountRateLimitOptions
        {
            Enabled = enabled, PermitLimit = permitLimit, WindowSeconds = windowSeconds,
        }), clock);

    private static async Task ReserveAsync(DistributedCacheLoginAttemptThrottle throttle, string email, int times)
    {
        for (var i = 0; i < times; i++)
            await throttle.TryReserveAsync(email);
    }

    [Fact]
    public async Task TryReserveAsync_is_not_blocked_for_a_fresh_account()
    {
        var throttle = Throttle(MemoryCache());
        var state = await throttle.TryReserveAsync("nobody@struo.test");
        state.IsBlocked.Should().BeFalse();
    }

    [Fact]
    public async Task TryReserveAsync_blocks_the_attempt_after_the_permit_limit()
    {
        var throttle = Throttle(MemoryCache(), permitLimit: 3);
        const string email = "spammed@struo.test";

        await ReserveAsync(throttle, email, 3);

        var state = await throttle.TryReserveAsync(email);
        state.IsBlocked.Should().BeTrue();
        state.RetryAfterSeconds.Should().BeGreaterThanOrEqualTo(1);
    }

    [Fact]
    public async Task TryReserveAsync_allows_the_last_attempt_within_the_limit()
    {
        var throttle = Throttle(MemoryCache(), permitLimit: 3);
        const string email = "almost@struo.test";

        await ReserveAsync(throttle, email, 2);

        (await throttle.TryReserveAsync(email)).IsBlocked.Should().BeFalse();
    }

    [Fact]
    public async Task Concurrent_reservations_are_each_counted_once()
    {
        var throttle = Throttle(MemoryCache(), permitLimit: 10);
        const string email = "parallel@struo.test";

        var states = await Task.WhenAll(Enumerable.Range(0, 50)
            .Select(_ => Task.Run(() => throttle.TryReserveAsync(email))));

        states.Count(s => !s.IsBlocked).Should().Be(10);
        states.Count(s => s.IsBlocked).Should().Be(40);
    }

    [Fact]
    public async Task ResetAsync_clears_a_blocked_account()
    {
        var throttle = Throttle(MemoryCache(), permitLimit: 1);
        const string email = "reset-me@struo.test";

        await ReserveAsync(throttle, email, 1);
        (await throttle.TryReserveAsync(email)).IsBlocked.Should().BeTrue();

        await throttle.ResetAsync(email);

        (await throttle.TryReserveAsync(email)).IsBlocked.Should().BeFalse();
    }

    [Fact]
    public async Task Throttling_is_per_account()
    {
        var throttle = Throttle(MemoryCache(), permitLimit: 1);

        await ReserveAsync(throttle, "account-a@struo.test", 1);

        (await throttle.TryReserveAsync("account-a@struo.test")).IsBlocked.Should().BeTrue();
        (await throttle.TryReserveAsync("account-b@struo.test")).IsBlocked.Should().BeFalse();
    }

    [Fact]
    public async Task Email_matching_is_case_and_whitespace_insensitive()
    {
        var throttle = Throttle(MemoryCache(), permitLimit: 1);

        await ReserveAsync(throttle, "  Spray@Struo.Test  ", 1);

        (await throttle.TryReserveAsync("spray@struo.test")).IsBlocked.Should().BeTrue();
    }

    [Fact]
    public async Task Disabled_never_blocks_regardless_of_attempt_count()
    {
        var throttle = Throttle(MemoryCache(), permitLimit: 1, enabled: false);
        const string email = "off@struo.test";

        await ReserveAsync(throttle, email, 3);

        (await throttle.TryReserveAsync(email)).IsBlocked.Should().BeFalse();
    }

    [Fact]
    public async Task Repeated_attempts_do_not_slide_the_window_forward()
    {
        // permitLimit=1 means the account is over budget from the FIRST attempt onward, for as long as
        // its window stays open — which is what makes this discriminate fixed-vs-sliding: if a SECOND
        // attempt, made while still inside the original window, restarted the window's clock, blocking
        // would still be in effect at t≈3.2s. Observing NOT-blocked there proves the window's expiry
        // stayed anchored to the FIRST attempt. The clock is a fake advanced by hand rather than a real
        // Task.Delay, so the test is instant and cannot flake on scheduler jitter. The cache entry's own
        // AbsoluteExpiration is computed from the fake clock but checked against the REAL wall clock,
        // which has barely moved, so it stays in the future and never interferes with the assertions.
        var clock = new FakeTimeProvider();
        var throttle = Throttle(MemoryCache(), permitLimit: 1, windowSeconds: 3, clock: clock);
        const string email = "fixed-window@struo.test";

        await throttle.TryReserveAsync(email);    // count=1, window starts at t=0, ends at t=3s
        clock.Advance(TimeSpan.FromSeconds(1.5)); // t≈1.5s — still inside the original window
        (await throttle.TryReserveAsync(email)).IsBlocked.Should().BeTrue();

        clock.Advance(TimeSpan.FromSeconds(1.7)); // t≈3.2s: past the ORIGINAL window's end
        (await throttle.TryReserveAsync(email)).IsBlocked.Should().BeFalse(
            "the window must have expired at its original 3s mark, not been pushed out by the blocked attempt");
    }

    [Fact]
    public async Task RetryAfterSeconds_decreases_as_the_window_elapses()
    {
        // A fake, hand-advanced clock removes real-clock timing concerns (CI jitter crossing the
        // window boundary): the advance below is exact, not a Task.Delay guess.
        var clock = new FakeTimeProvider();
        var throttle = Throttle(MemoryCache(), permitLimit: 1, windowSeconds: 3, clock: clock);
        const string email = "decreasing@struo.test";

        await throttle.TryReserveAsync(email);
        var first = await throttle.TryReserveAsync(email);
        first.IsBlocked.Should().BeTrue();

        clock.Advance(TimeSpan.FromSeconds(1.5));
        var second = await throttle.TryReserveAsync(email);
        second.IsBlocked.Should().BeTrue();

        second.RetryAfterSeconds.Should().BeLessThan(first.RetryAfterSeconds);
    }
}
