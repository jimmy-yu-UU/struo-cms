using Struo.Application.Security;

namespace Struo.Infrastructure.Security;

/// <summary>
/// Phase 2 allow-all permission policy (no gating). Replaced by real RBAC in Phase 6;
/// not intended for production use.
/// </summary>
public sealed class AllowAllPermissionService : IPermissionService
{
    public bool CanRead(string collection) => true;
    public bool CanWrite(string collection) => true;
    public bool CanDelete(string collection) => true;
    public bool IsSuperAdmin => true;
    public IReadOnlyCollection<string> ReadableFields(string collection, IEnumerable<string> allFieldNames) =>
        allFieldNames.ToList();
}
