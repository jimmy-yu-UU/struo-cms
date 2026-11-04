namespace Struo.Application.Security;

/// <summary>Canonical stored form of a user email: trimmed and lowercased (invariant culture). The login
/// throttles key their counters on this form, so anything that addresses a login lock by email (the
/// <c>login-locks</c> endpoints, the <c>auth:unlock</c> command) normalizes through it too.</summary>
public static class EmailAddress
{
    private const int MaxLength = 320;

    public static string Normalize(string email) => email.Trim().ToLowerInvariant();

    /// <summary>Whether <paramref name="email"/> can address a login lock: not blank and at most 320
    /// characters once trimmed. A lock is keyed on the string alone, and an account can hold any non-blank
    /// email, so this checks nothing about the address's shape.</summary>
    public static bool IsUsable(string? email) =>
        !string.IsNullOrWhiteSpace(email) && email.Trim().Length <= MaxLength;
}
