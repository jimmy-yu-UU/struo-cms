namespace Struo.Application.Security;

/// <summary>Canonical stored form of a user email: trimmed and lowercased (invariant culture). The login
/// throttles key their counters on this form, so anything that addresses a login lock by email (the
/// <c>login-locks</c> endpoints, the <c>auth:unlock</c> command) normalizes through it too.</summary>
public static class EmailAddress
{
    private const int MaxLength = 320;

    public static string Normalize(string email) => email.Trim().ToLowerInvariant();

    /// <summary>Whether <paramref name="email"/> has the minimal shape of an address: one <c>@</c> with text
    /// on both sides, no whitespace inside, at most 320 characters. It exists to reject obvious garbage, not
    /// to validate deliverability.</summary>
    public static bool IsWellFormed(string? email)
    {
        if (string.IsNullOrWhiteSpace(email)) return false;
        var trimmed = email.Trim();
        var at = trimmed.IndexOf('@');
        return trimmed.Length <= MaxLength
            && at > 0 && at < trimmed.Length - 1
            && trimmed.IndexOf('@', at + 1) < 0
            && !trimmed.Any(char.IsWhiteSpace);
    }
}
