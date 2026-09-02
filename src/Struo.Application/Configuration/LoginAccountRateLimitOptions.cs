namespace Struo.Application.Configuration;

/// <summary>Config-bound tuning for the app-layer, per-account login throttle
/// (<see cref="Security.ILoginAttemptThrottle"/>), backed by <c>IDistributedCache</c> and applied
/// ONLY to <c>POST /api/auth/login</c>, ahead of <c>IAuthService.AuthenticateAsync</c>. Bound from the
/// <c>RateLimiting:LoginAccount</c> configuration section; absence of the section keeps these
/// defaults.
/// <para>
/// This is the layer <c>LoginRateLimitOptions</c> (per client IP) cannot be: it counts failures by the
/// account being attempted, not by caller, so it still catches a slow password-spray against one
/// account from many source IPs, and — unlike the per-IP limiter — it never collapses an entire
/// office's staff into one shared bucket, because each account gets its own counter regardless of who
/// is attempting it or from where.
/// </para></summary>
public sealed class LoginAccountRateLimitOptions
{
    public const string SectionName = "RateLimiting:LoginAccount";

    /// <summary>Whether the per-account login throttle is active. Defaults to <c>true</c>.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Max FAILED login attempts allowed against one account within
    /// <see cref="WindowSeconds"/>. A successful login resets the counter, so a legitimate user
    /// cannot lock themselves out by logging in normally. Deliberately generous relative to the
    /// per-IP limiter's 5/60s — this layer targets slow, sustained password-spraying against one
    /// account, not burst traffic, and 10 attempts leaves headroom for a real user mistyping their
    /// password a few times.</summary>
    public int PermitLimit { get; set; } = 10;

    /// <summary>Fixed-window length, in seconds. Deliberately much longer than the per-IP limiter's
    /// 60s window, for the same reason as <see cref="PermitLimit"/>.</summary>
    public int WindowSeconds { get; set; } = 900;
}
