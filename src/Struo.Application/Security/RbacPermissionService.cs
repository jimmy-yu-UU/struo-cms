namespace Struo.Application.Security;

/// <summary>
/// Real RBAC policy: reads the per-request <see cref="ICurrentPermissions"/> snapshot that
/// <c>PermissionResolutionMiddleware</c> resolves once per request, so <see cref="IPermissionService"/>'s
/// checks can stay synchronous. This is the only <see cref="IPermissionService"/> implementation
/// shipped; the test project has its own allow-all stub for test setup. Field-level control is out of
/// scope.
/// </summary>
public sealed class RbacPermissionService(ICurrentPermissions current) : IPermissionService
{
    public bool CanRead(string collection) => current.Current.CanRead(collection);
    public bool CanWrite(string collection) => current.Current.CanWrite(collection);
    public bool CanDelete(string collection) => current.Current.CanDelete(collection);
    public bool IsSuperAdmin => current.Current.IsSuperAdmin;

    public IReadOnlyCollection<string> ReadableFields(string collection, IEnumerable<string> allFieldNames) =>
        allFieldNames.ToList();
}
