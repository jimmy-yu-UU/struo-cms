namespace Struo.Api.Auth;

/// <summary>System collections that require authentication for ALL access (read included),
/// even before RBAC (6b). 6b's per-collection permission model later subsumes this set.</summary>
public static class ProtectedCollections
{
    public static readonly HashSet<string> Set = new(StringComparer.OrdinalIgnoreCase) { "user" };
}
