namespace Struo.Application.Security;

/// <summary>Folds raw role/permission rows into an <see cref="EffectivePermissions"/> snapshot.</summary>
public static class PermissionResolver
{
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
        return new EffectivePermissions(isSuperAdmin: false, byCollection);
    }
}
