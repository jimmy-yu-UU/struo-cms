using Struo.Application.Configuration;

namespace Struo.Application.Security;

/// <summary>Single home for the plaintext-password rules. Every request-path WRITE that accepts a
/// password routes through <see cref="Validate"/> — inlining the check anywhere else lets those paths
/// drift (which is exactly what this type replaced).
/// <para>
/// The one deliberate exception is <c>AdminUserSeeder</c>: it hashes <c>Auth:BootstrapAdmin:Password</c>
/// straight from config, bypassing this validator, so a fresh install always has a login even though the
/// shipped default (<c>"admin"</c>, 5 characters) would otherwise fail this very policy. That bypass is
/// intentional and out of scope for this type to close.
/// </para></summary>
public static class PasswordPolicy
{
    /// <summary>Returns null when the password satisfies the policy, otherwise a caller-facing
    /// message. For a too-short password this string is a fallback: the SPA's own check runs first
    /// and composes localized text from the published <see cref="PasswordPolicyOptions.MinLength"/>,
    /// so this string only surfaces if that local check is bypassed. For a too-long password there
    /// is no such fallback — <see cref="PasswordPolicyOptions.MaxLength"/> is deliberately not
    /// published (see its own remarks), so the client has nothing to compose from and this string is
    /// the sole user-facing explanation of that bound.</summary>
    public static string? Validate(string? password, PasswordPolicyOptions policy)
    {
        if (string.IsNullOrEmpty(password) || password.Length < policy.MinLength)
            return $"Password must be at least {policy.MinLength} characters.";
        if (password.Length > policy.MaxLength)
            return $"Password must be at most {policy.MaxLength} characters.";
        return null;
    }
}
