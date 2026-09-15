namespace Struo.Application.Configuration;

/// <summary>Config-bound tuning for the app-layer, per-CLIENT-IP login rate limiter (fixed-window;
/// applied ONLY to <c>POST /api/auth/login</c> via <c>[EnableRateLimiting("login")]</c>). Bound from
/// the <c>RateLimiting:Login</c> configuration section; absence of the section keeps these defaults.
/// Deliberately login-only — not a global limiter — since volumetric/global throttling is handled at
/// the web-server edge.
/// <para>
/// This is one of TWO independent login defenses, not the only one:
/// <see cref="LoginAccountRateLimitOptions"/> throttles by the ACCOUNT being attempted (via
/// <see cref="Struo.Application.Security.ILoginAttemptThrottle"/>) and is on by default, precisely because it does not have
/// this limiter's shared-egress-IP failure mode — see this class's <see cref="Enabled"/> doc.
/// </para></summary>
public sealed class LoginRateLimitOptions
{
    public const string SectionName = "RateLimiting:Login";

    /// <summary>Whether the in-app, per-client-IP login rate limiter is active. Defaults to
    /// <c>false</c>: admin-backend users are typically an organization's own staff sharing one NAT
    /// egress IP, so partitioning by client IP collapses an entire office into a single shared
    /// bucket — a default that would harm availability (staff locking each other out) rather than
    /// stopping a distributed attacker, without even requiring a misconfigured reverse proxy to do
    /// so. Enabling it is the right call only for a single-instance, directly-reachable deployment
    /// with no edge/WAF in front of it whose users do not share an egress IP (a personal or
    /// single-tenant install, or a development host) — see chapter 16 of the manual for the full
    /// guidance, including the reverse-proxy precondition it needs if enabled behind one.
    /// <see cref="LoginAccountRateLimitOptions"/> is the layer that stays on by default, since a
    /// per-account counter cannot suffer this collapse: every account gets its own bucket regardless
    /// of which IP is attempting it.</summary>
    public bool Enabled { get; set; } = false;

    /// <summary>Max login attempts allowed per client IP within <see cref="WindowSeconds"/>.</summary>
    public int PermitLimit { get; set; } = 5;

    /// <summary>Fixed-window length, in seconds.</summary>
    public int WindowSeconds { get; set; } = 60;
}
