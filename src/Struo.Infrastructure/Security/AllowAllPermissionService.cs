using Struo.Application.Security;

namespace Struo.Infrastructure.Security;

public sealed class AllowAllPermissionService : IPermissionService
{
    public bool CanRead(string collection) => true;
    public bool CanWrite(string collection) => true;
    public bool CanDelete(string collection) => true;
    public IReadOnlyCollection<string> ReadableFields(string collection, IEnumerable<string> allFieldNames) =>
        allFieldNames.ToList();
}
