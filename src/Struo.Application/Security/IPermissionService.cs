// src/Struo.Application/Security/IPermissionService.cs
namespace Struo.Application.Security;

public interface IPermissionService
{
    bool CanRead(string collection);
    bool CanWrite(string collection);
    bool CanDelete(string collection);
    IReadOnlyCollection<string> ReadableFields(string collection, IEnumerable<string> allFieldNames);

    /// <summary>
    /// True when the caller is a super-admin. Writes to <c>AdminOnly</c> collections
    /// (identity/authorization tables) require this, so a delegated per-collection write grant
    /// cannot be escalated into super-admin. Defaults to false (deny) for any implementation that
    /// does not model super-admin explicitly.
    /// </summary>
    bool IsSuperAdmin => false;
}
