namespace Struo.Application.Configuration;

/// <summary>Config-bound cap on how many <c>POST /api/auth/login</c> requests may be in flight at once
/// in this process. Every login verifies a password with Argon2id (memory-hard, CPU-heavy), so a flood
/// of parallel attempts is bounded here regardless of source IP or account. Bound from the
/// <c>RateLimiting:LoginConcurrency</c> configuration section; absence of the section keeps these
/// defaults. The cap is per process: each replica enforces its own.</summary>
public sealed class LoginConcurrencyOptions
{
    public const string SectionName = "RateLimiting:LoginConcurrency";

    /// <summary>Whether the concurrency cap is active. Defaults to <c>true</c>.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Logins allowed to verify a password simultaneously.</summary>
    public int PermitLimit { get; set; } = 4;

    /// <summary>Logins allowed to wait for a free slot; further requests are rejected with 429.</summary>
    public int QueueLimit { get; set; } = 32;
}
