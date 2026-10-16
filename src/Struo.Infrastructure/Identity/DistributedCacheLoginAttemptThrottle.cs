using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Options;
using Struo.Application.Configuration;
using Struo.Application.Security;

namespace Struo.Infrastructure.Identity;

/// <summary>
/// <see cref="ILoginAttemptThrottle"/> backed by <c>IDistributedCache</c> (an in-memory cache unless
/// Redis is configured, in which case the Redis-backed throttle is used instead).
/// <para>
/// Fixed window, stored as a single <c>{ Count, WindowStartUnixSeconds }</c> payload per account, with
/// <see cref="DistributedCacheEntryOptions.AbsoluteExpiration"/> set to the window's own end. The
/// window's boundary is fixed at the first attempt in it, and later attempts never push it forward.
/// </para>
/// <para>
/// <c>IDistributedCache</c> has no atomic read-modify-write, so <see cref="TryReserveAsync"/> reads the
/// count and writes back count+1 under a per-account in-process lock (a fixed set of striped locks, so
/// memory stays bounded). Concurrent attempts against one account inside this process are serialized
/// and each is counted exactly once. The lock cannot span processes: where several instances share a
/// distributed cache that is not Redis, concurrent attempts landing on different instances can read
/// the same count and undercount, admitting a few extra guesses per window.
/// </para>
/// </summary>
public sealed class DistributedCacheLoginAttemptThrottle(
    IDistributedCache cache, IOptions<LoginAccountRateLimitOptions> options, TimeProvider? clock = null)
    : ILoginAttemptThrottle
{
    private const string KeyPrefix = "login-throttle:";
    private const int LockStripes = 256;

    private static readonly SemaphoreSlim[] Locks =
        Enumerable.Range(0, LockStripes).Select(_ => new SemaphoreSlim(1, 1)).ToArray();

    // Trailing optional so DI resolves it without a registered TimeProvider; tests pass a fake to
    // advance the clock without a real Task.Delay.
    private readonly TimeProvider _clock = clock ?? TimeProvider.System;

    private sealed record WindowState(int Count, long WindowStartUnixSeconds);

    public async Task<LoginThrottleState> TryReserveAsync(string email, CancellationToken ct = default)
    {
        var opts = options.Value;
        if (!opts.Enabled) return new LoginThrottleState(false, 0);

        var key = CacheKey(email);
        var gate = Locks[(uint)key.GetHashCode() % LockStripes];
        await gate.WaitAsync(ct);
        try
        {
            var now = _clock.GetUtcNow().ToUnixTimeSeconds();
            var state = await ReadAsync(key, ct);

            // A missing or expired window starts a fresh one at `now`.
            if (state is null || now >= state.WindowStartUnixSeconds + opts.WindowSeconds)
            {
                await WriteAsync(key, new WindowState(1, now), now + opts.WindowSeconds, ct);
                return new LoginThrottleState(false, 0);
            }

            var windowEnd = state.WindowStartUnixSeconds + opts.WindowSeconds;
            if (state.Count >= opts.PermitLimit)
                return new LoginThrottleState(true, (int)Math.Max(1, windowEnd - now));

            await WriteAsync(key, state with { Count = state.Count + 1 }, windowEnd, ct);
            return new LoginThrottleState(false, 0);
        }
        finally
        {
            gate.Release();
        }
    }

    public Task ResetAsync(string email, CancellationToken ct = default) =>
        options.Value.Enabled ? cache.RemoveAsync(CacheKey(email), ct) : Task.CompletedTask;

    private async Task<WindowState?> ReadAsync(string key, CancellationToken ct)
    {
        var bytes = await cache.GetAsync(key, ct);
        return bytes is null ? null : JsonSerializer.Deserialize<WindowState>(bytes);
    }

    private Task WriteAsync(string key, WindowState state, long expiresAtUnixSeconds, CancellationToken ct) =>
        cache.SetAsync(
            key, JsonSerializer.SerializeToUtf8Bytes(state),
            new DistributedCacheEntryOptions
            {
                AbsoluteExpiration = DateTimeOffset.FromUnixTimeSeconds(expiresAtUnixSeconds),
            }, ct);

    // Never put the raw email in a cache key — hash the normalized (trimmed, lowercased) address so a
    // Redis operator with read access to key names cannot enumerate attempted account emails.
    private static string CacheKey(string email) =>
        KeyPrefix + Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(email.Trim().ToLowerInvariant())));
}
