namespace Struo.Application.Security;

/// <summary>
/// Real RBAC policy: reads the per-request <see cref="ICurrentPermissions"/> snapshot. Keeps the
/// synchronous, ambient <see cref="IPermissionService"/> signature so <c>ItemService</c> is unchanged.
/// Replaces <c>AllowAllPermissionService</c>. Field-level control is out of scope (Phase 6b §1).
/// </summary>
public sealed class RbacPermissionService(ICurrentPermissions current) : IPermissionService
{
    public bool CanRead(string collection) => current.Current.CanRead(collection);
    public bool CanWrite(string collection) => current.Current.CanWrite(collection);
    public bool CanDelete(string collection) => current.Current.CanDelete(collection);

    public IReadOnlyCollection<string> ReadableFields(string collection, IEnumerable<string> allFieldNames) =>
        allFieldNames.ToList();
}
