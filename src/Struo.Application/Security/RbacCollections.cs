namespace Struo.Application.Security;

/// <summary>The framework's role-management collections, named once. <c>userRole</c> and <c>permission</c>
/// are hidden junction collections: they carry no grant of their own and follow the collection that owns
/// them, so a role assignment is governed by write on <c>user</c> and a role's permission grants by
/// write on <c>role</c>.</summary>
public static class RbacCollections
{
    public const string Role = "role";
    public const string UserRole = "userRole";
    public const string Permission = "permission";

    /// <summary>The collection whose grant governs <paramref name="collection"/>, or null when it has its
    /// own grant.</summary>
    public static string? GoverningCollection(string collection)
    {
        if (string.Equals(collection, UserRole, StringComparison.OrdinalIgnoreCase)) return UserCollection.Name;
        if (string.Equals(collection, Permission, StringComparison.OrdinalIgnoreCase)) return Role;
        return null;
    }
}
