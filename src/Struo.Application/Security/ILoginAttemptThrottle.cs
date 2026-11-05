namespace Struo.Application.Security;

/// <summary>The result of reserving a login attempt for one account.</summary>
/// <param name="IsBlocked">Whether the caller must be refused before spending any authentication
/// cost (e.g. Argon2id CPU) on this attempt.</param>
/// <param name="RetryAfterSeconds">Seconds until the current window ends, floored at 1 whenever
/// <paramref name="IsBlocked"/> is <c>true</c>. Meaningless (and left at 0) when not blocked.</param>
public sealed record LoginThrottleState(bool IsBlocked, int RetryAfterSeconds);

/// <summary>Per-account login throttle, counting login attempts against one account within a
/// fixed window regardless of which client IP they came from. An attempt is counted when it is
/// reserved, before the password is verified, so concurrent guesses cannot all observe the same
/// count; a successful login resets the count. Complements — does not replace — the per-client-IP
/// limiter (<c>Struo.Application.Configuration.LoginRateLimitOptions</c>): that one cannot see the
/// request body (the rate limiter's partitioner runs before model binding), so it has no way to key
/// on the account being attempted; this one can, because <c>AuthController.Login</c> calls it after
/// the body is bound. See <c>Struo.Application.Configuration.LoginAccountRateLimitOptions</c> for the
/// config-bound tuning.</summary>
public interface ILoginAttemptThrottle
{
    /// <summary>Counts one attempt against <paramref name="email"/> and reports whether it may proceed.
    /// Must be called before spending any authentication cost on the request. A blocked attempt is
    /// refused without being verified.</summary>
    Task<LoginThrottleState> TryReserveAsync(string email, CancellationToken ct = default);

    /// <summary>Clears the attempt count for <paramref name="email"/>, called on a successful
    /// login so a legitimate user cannot lock themselves out by logging in normally.</summary>
    Task ResetAsync(string email, CancellationToken ct = default);

    /// <summary>Reports whether <paramref name="email"/> is locked, without counting an attempt. Locked
    /// means the next <see cref="TryReserveAsync"/> would be refused: the window's permit limit is
    /// already reached. <see cref="LoginThrottleState.RetryAfterSeconds"/> is the time left in the window
    /// (at least 1) when locked, and 0 otherwise. Never locked while the throttle is disabled.</summary>
    Task<LoginThrottleState> GetStateAsync(string email, CancellationToken ct = default);
}
