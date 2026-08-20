namespace Struo.Application.Configuration;

/// <summary>Config-bound tuning for the change-password rate limiter (fixed-window, partitioned by
/// the AUTHENTICATED user id; applied only to <c>PUT /api/users/{id}/password</c> via
/// <c>[EnableRateLimiting("password")]</c>). Bound from <c>RateLimiting:Password</c>; absence of the
/// section keeps these defaults.
/// <para>
/// Partitioned by user id rather than client IP — unlike the anonymous login endpoint, this one has a
/// caller identity available, which cannot be sprayed across buckets by an attacker and does not
/// collapse to a single bucket behind a reverse proxy the way <c>RemoteIpAddress</c> does.
/// </para></summary>
public sealed class PasswordRateLimitOptions
{
    public const string SectionName = "RateLimiting:Password";

    /// <summary>Whether the limiter is active. Defaults to <c>true</c>. Set to <c>false</c> in
    /// multi-pod deployments where this in-memory, per-pod limiter cannot enforce a global limit.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Max attempts per authenticated user within <see cref="WindowSeconds"/>. A super-admin
    /// doing bulk resets shares this budget and will be throttled past it — accepted, and the reason
    /// the value is configurable.</summary>
    public int PermitLimit { get; set; } = 5;

    /// <summary>Fixed-window length, in seconds.</summary>
    public int WindowSeconds { get; set; } = 60;
}
