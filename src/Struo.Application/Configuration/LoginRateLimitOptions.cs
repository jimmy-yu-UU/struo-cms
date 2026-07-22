namespace Struo.Application.Configuration;

/// <summary>SEC-7: config-bound tuning for the app-layer login rate limiter (fixed-window,
/// partitioned by client IP; applied ONLY to <c>POST /api/auth/login</c> via
/// <c>[EnableRateLimiting("login")]</c>). Bound from the <c>RateLimiting:Login</c> configuration
/// section; absence of the section keeps these defaults. Deliberately login-only — not a global
/// limiter — per the SEC-7 remediation decision; volumetric/global throttling is handled at the
/// web-server edge.</summary>
public sealed class LoginRateLimitOptions
{
    public const string SectionName = "RateLimiting:Login";

    /// <summary>Max login attempts allowed per client IP within <see cref="WindowSeconds"/>.</summary>
    public int PermitLimit { get; set; } = 5;

    /// <summary>Fixed-window length, in seconds.</summary>
    public int WindowSeconds { get; set; } = 60;
}
