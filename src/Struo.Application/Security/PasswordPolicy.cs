using Struo.Application.Configuration;

namespace Struo.Application.Security;

/// <summary>Single home for the plaintext-password rules. Every call site that accepts a password
/// routes through <see cref="Validate"/> — inlining the check anywhere else lets the two paths drift
/// (which is exactly what this type replaced).</summary>
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
