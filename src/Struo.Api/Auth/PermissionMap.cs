// src/Struo.Api/Auth/PermissionMap.cs
using Struo.Application.Metadata;
using Struo.Application.Security;

namespace Struo.Api.Auth;

/// <summary>Builds the per-collection read/write/delete map the account endpoints return.</summary>
internal static class PermissionMap
{
    /// <summary>
    /// Maps each collection the caller can read, write or delete to those three flags. Empty for a
    /// super admin, whose access is signalled by <c>isSuperAdmin</c> instead.
    /// </summary>
    internal static Dictionary<string, object> Build(EffectivePermissions eff, SchemaService schema)
    {
        var map = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
        if (eff.IsSuperAdmin) return map;
        foreach (var collectionName in schema.GetAll().Select(c => c.Name))
        {
            var read = eff.CanRead(collectionName);
            var write = eff.CanWrite(collectionName);
            var del = eff.CanDelete(collectionName);
            if (read || write || del)
                map[collectionName] = new { read, write, @delete = del };
        }
        return map;
    }
}
