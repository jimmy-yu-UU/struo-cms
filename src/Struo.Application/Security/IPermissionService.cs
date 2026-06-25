// src/Struo.Application/Security/IPermissionService.cs
namespace Struo.Application.Security;

public interface IPermissionService
{
    bool CanRead(string collection);
    bool CanWrite(string collection);
    bool CanDelete(string collection);
    IReadOnlyCollection<string> ReadableFields(string collection, IEnumerable<string> allFieldNames);
}
