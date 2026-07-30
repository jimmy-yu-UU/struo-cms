namespace Struo.Application.Configuration;

/// <summary>Config-bound tuning for the app-layer login rate limiter (fixed-window,
/// partitioned by client IP; applied ONLY to <c>POST /api/auth/login</c> via
/// <c>[EnableRateLimiting("login")]</c>). Bound from the <c>RateLimiting:Login</c> configuration
/// section; absence of the section keeps these defaults. Deliberately login-only — not a global
/// limiter — since volumetric/global throttling is handled at the
/// web-server edge.</summary>
public sealed class LoginRateLimitOptions
{
    public const string SectionName = "RateLimiting:Login";

    /// <summary>Whether the in-app login rate limiter is active. Defaults to <c>true</c>
    /// (secure-by-default for direct/single-instance deployments). Set to <c>false</c> in
    /// multi-pod deployments (e.g. Kubernetes) where per-IP rate limiting is instead enforced at
    /// the ingress/edge/WAF, which sees the real client IP and sits in front of every pod — unlike
    /// this in-memory, per-pod limiter, which cannot enforce a true global limit across replicas.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Max login attempts allowed per client IP within <see cref="WindowSeconds"/>.</summary>
    public int PermitLimit { get; set; } = 5;

    /// <summary>Fixed-window length, in seconds.</summary>
    public int WindowSeconds { get; set; } = 60;
}
