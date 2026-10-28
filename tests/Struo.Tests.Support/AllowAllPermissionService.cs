using Struo.Application.Security;

namespace Struo.Tests.Support;

/// <summary>
/// Test-only permission policy that permits everything (and reports super-admin). Lives in the test
/// project so the shippable Infrastructure assembly carries no auth-bypass type that a stray
/// <c>services.Replace</c> could accidentally re-enable. Production uses
/// <c>RbacPermissionService</c>.
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
