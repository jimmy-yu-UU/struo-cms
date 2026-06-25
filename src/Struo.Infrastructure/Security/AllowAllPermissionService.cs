using Struo.Application.Security;

namespace Struo.Infrastructure.Security;

/// <summary>
/// Development/test scaffold that grants every permission unconditionally.
/// DO NOT register in production — use a real <see cref="IPermissionService"/> implementation instead.
/// </summary>
[Obsolete("Development scaffold only — do not register in production.")]
public sealed class AllowAllPermissionService : IPermissionService
{
    public bool CanRead(string collection) => true;
    public bool CanWrite(string collection) => true;
    public bool CanDelete(string collection) => true;
    public IReadOnlyCollection<string> ReadableFields(string collection, IEnumerable<string> allFieldNames) =>
        allFieldNames.ToList();
}
