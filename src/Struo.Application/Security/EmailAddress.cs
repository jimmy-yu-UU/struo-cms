namespace Struo.Application.Security;

/// <summary>Canonical stored form of a user email: trimmed and lowercased (invariant culture). The login
/// throttles key their counters on this form, so anything that addresses a login lock by email (the
/// <c>login-locks</c> endpoints, the <c>auth:unlock</c> command) normalizes through it too.</summary>
public static class EmailAddress
{
    /// <summary>The length of the <c>email</c> column of the users table; the core schema migration sizes
    /// the column from it, and <see cref="IsUsable"/> caps input at it.</summary>
    public const int MaxLength = 255;

    public static string Normalize(string email) => email.Trim().ToLowerInvariant();

    /// <summary>Whether <paramref name="email"/> can address a login lock: not blank and at most
    /// <see cref="MaxLength"/> characters once trimmed. A lock is keyed on the string alone, so this checks
    /// nothing about the address's shape; <c>POST /api/users</c> applies the Email field rule when it creates
    /// the account.</summary>
    public static bool IsUsable(string? email) =>
        !string.IsNullOrWhiteSpace(email) && email.Trim().Length <= MaxLength;
}
