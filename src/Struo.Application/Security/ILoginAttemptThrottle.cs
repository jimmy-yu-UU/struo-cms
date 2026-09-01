namespace Struo.Application.Security;

/// <summary>The result of a throttle check for one account's login attempts.</summary>
/// <param name="IsBlocked">Whether the caller must be refused before spending any authentication
/// cost (e.g. Argon2id CPU) on this attempt.</param>
/// <param name="RetryAfterSeconds">Seconds until the current window ends, floored at 1 whenever
/// <paramref name="IsBlocked"/> is <c>true</c>. Meaningless (and left at 0) when not blocked.</param>
public sealed record LoginThrottleState(bool IsBlocked, int RetryAfterSeconds);

/// <summary>Per-account login throttle, counting FAILED login attempts against one account within a
/// fixed window regardless of which client IP they came from. Complements — does not replace — the
/// per-client-IP limiter (<c>Struo.Application.Configuration.LoginRateLimitOptions</c>): that one
/// cannot see the request body (the rate limiter's partitioner runs before model binding), so it has
/// no way to key on the account being attempted; this one can, because <c>AuthController.Login</c>
/// calls it after the body is bound. See
/// <c>Struo.Application.Configuration.LoginAccountRateLimitOptions</c> for the config-bound tuning.</summary>
public interface ILoginAttemptThrottle
{
    /// <summary>Reports whether <paramref name="email"/> is currently blocked, WITHOUT recording an
    /// attempt. Must be called before spending any authentication cost on the request.</summary>
    Task<LoginThrottleState> CheckAsync(string email, CancellationToken ct = default);

    /// <summary>Records one failed login attempt against <paramref name="email"/>.</summary>
    Task RecordFailureAsync(string email, CancellationToken ct = default);

    /// <summary>Clears any recorded failures for <paramref name="email"/>, called on a successful
    /// login so a legitimate user cannot lock themselves out by logging in normally.</summary>
    Task ResetAsync(string email, CancellationToken ct = default);
}
