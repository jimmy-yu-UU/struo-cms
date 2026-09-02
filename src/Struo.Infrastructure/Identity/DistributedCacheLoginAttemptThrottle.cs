using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Options;
using Struo.Application.Configuration;
using Struo.Application.Security;

namespace Struo.Infrastructure.Identity;

/// <summary>
/// <see cref="ILoginAttemptThrottle"/> backed by <c>IDistributedCache</c> (Redis when configured, an
/// in-memory cache otherwise — the same store <c>DistributedCacheTicketStore</c> uses, wired by
/// <c>AuthWiring</c>).
/// <para>
/// Fixed window, stored as a single <c>{ Count, WindowStartUnixSeconds }</c> payload per account, with
/// <see cref="DistributedCacheEntryOptions.AbsoluteExpiration"/> set to the window's own end. This is
/// deliberate: <c>IDistributedCache</c> exposes no sliding/refresh-on-read primitive here, so the
/// window's boundary is fixed at whichever moment the FIRST failure in it was recorded, and repeated
/// failures never push that boundary forward. Once the entry expires the account starts a fresh
/// window on its very next failure.
/// </para>
/// <para>
/// <c>IDistributedCache</c> has no atomic read-modify-write (no compare-and-swap, no native INCR
/// primitive independent of the concrete store). <see cref="RecordFailureAsync"/> therefore reads the
/// current count and writes back count+1 as two separate operations, so two failing requests against
/// the SAME account arriving concurrently can both read the same starting count and each write back
/// the same incremented value, losing one increment. This is an accepted, deliberate trade-off for a
/// throttle, not a bug to fix here: undercounting costs a handful of extra guesses at the boundary, not
/// a bypass, and a truly atomic counter would tie this abstraction to a store-specific primitive (e.g.
/// Redis <c>INCR</c>) it is deliberately kept independent of.
/// </para>
/// </summary>
public sealed class DistributedCacheLoginAttemptThrottle(
    IDistributedCache cache, IOptions<LoginAccountRateLimitOptions> options)
    : ILoginAttemptThrottle
{
    private const string KeyPrefix = "login-throttle:";

    private sealed record WindowState(int Count, long WindowStartUnixSeconds);

    public async Task<LoginThrottleState> CheckAsync(string email, CancellationToken ct = default)
    {
        var opts = options.Value;
        if (!opts.Enabled) return new LoginThrottleState(false, 0);

        var state = await ReadAsync(email, ct);
        if (state is null) return new LoginThrottleState(false, 0);

        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var windowEnd = state.WindowStartUnixSeconds + opts.WindowSeconds;
        if (now >= windowEnd || state.Count < opts.PermitLimit)
            return new LoginThrottleState(false, 0);

        return new LoginThrottleState(true, (int)Math.Max(1, windowEnd - now));
    }

    public async Task RecordFailureAsync(string email, CancellationToken ct = default)
    {
        var opts = options.Value;
        if (!opts.Enabled) return;

        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var state = await ReadAsync(email, ct);

        // A missing or already-expired window starts a fresh one at `now`; an active window keeps
        // its original start (see class doc — the window never slides forward on repeated failures).
        var (windowStart, count) = state is null || now >= state.WindowStartUnixSeconds + opts.WindowSeconds
            ? (now, 1)
            : (state.WindowStartUnixSeconds, state.Count + 1);

        await WriteAsync(
            email, new WindowState(count, windowStart),
            DateTimeOffset.FromUnixTimeSeconds(windowStart + opts.WindowSeconds), ct);
    }

    public Task ResetAsync(string email, CancellationToken ct = default) =>
        options.Value.Enabled ? cache.RemoveAsync(CacheKey(email), ct) : Task.CompletedTask;

    private async Task<WindowState?> ReadAsync(string email, CancellationToken ct)
    {
        var bytes = await cache.GetAsync(CacheKey(email), ct);
        return bytes is null ? null : JsonSerializer.Deserialize<WindowState>(bytes);
    }

    private Task WriteAsync(string email, WindowState state, DateTimeOffset expiresAt, CancellationToken ct) =>
        cache.SetAsync(
            CacheKey(email), JsonSerializer.SerializeToUtf8Bytes(state),
            new DistributedCacheEntryOptions { AbsoluteExpiration = expiresAt }, ct);

    // Never put the raw email in a cache key — hash the normalized (trimmed, lowercased) address so a
    // Redis operator with read access to key names cannot enumerate attempted account emails.
    private static string CacheKey(string email) =>
        KeyPrefix + Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(email.Trim().ToLowerInvariant())));
}
