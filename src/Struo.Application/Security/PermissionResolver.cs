namespace Struo.Application.Security;

/// <summary>Folds raw role/permission/capability rows into an <see cref="EffectivePermissions"/> snapshot.</summary>
public static class PermissionResolver
{
    /// <summary>Name of the role every caller holds, anonymous ones included. It never contributes a
    /// capability, whichever store produced the rows.</summary>
    public const string PublicRoleName = "public";

    public static EffectivePermissions Resolve(RolePermissionData data)
    {
        if (data.Roles.Any(r => r.IsSuperAdmin))
            return new EffectivePermissions(isSuperAdmin: true,
                new Dictionary<string, (bool, bool, bool)>());

        var byCollection = new Dictionary<string, (bool Read, bool Write, bool Delete)>(
            StringComparer.OrdinalIgnoreCase);
        foreach (var p in data.Permissions)
        {
            byCollection.TryGetValue(p.Collection, out var cur);
            byCollection[p.Collection] =
                (cur.Read || p.CanRead, cur.Write || p.CanWrite, cur.Delete || p.CanDelete);
        }
        var publicRoleIds = data.Roles.Where(r => r.Name == PublicRoleName).Select(r => r.Id).ToHashSet();
        var capabilities = data.Capabilities
            .Where(c => !publicRoleIds.Contains(c.RoleId))
            .Select(c => c.Capability)
            .ToHashSet(StringComparer.Ordinal);
        return new EffectivePermissions(isSuperAdmin: false, byCollection, capabilities);
    }
}
