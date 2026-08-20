using Struo.Application.Configuration;

namespace Struo.Application.Security;

/// <summary>Single home for the plaintext-password rules. Every REQUEST-PATH call site that accepts a
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
    /// message. The message is a fallback only: the SPA composes its own localized text from the
    /// published minimum, so this string must never be the only way a user learns the rule.</summary>
    public static string? Validate(string? password, PasswordPolicyOptions policy)
    {
        if (string.IsNullOrEmpty(password) || password.Length < policy.MinLength)
            return $"Password must be at least {policy.MinLength} characters.";
        if (password.Length > policy.MaxLength)
            return $"Password must be at most {policy.MaxLength} characters.";
        return null;
    }
}
