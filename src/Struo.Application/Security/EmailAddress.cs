namespace Struo.Application.Security;

/// <summary>Canonical stored form of a user email: trimmed and lowercased (invariant culture).</summary>
public static class EmailAddress
{
    public static string Normalize(string email) => email.Trim().ToLowerInvariant();
}
