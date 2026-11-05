// src/Struo.Application/Query/Write/RoleWriteInterceptor.cs
using Struo.Application.Security;

namespace Struo.Application.Query;

/// <summary>
/// The <c>role</c> collection's purge rule: the role's permission and capability rows go in the same
/// transaction as the role row. Its user-role junction rows are already removed by the purge pipeline,
/// as the target side of <c>User.Roles</c>. Resolution reads only existing roles, so no cached state
/// names a purged role.
/// </summary>
public sealed class RoleWriteInterceptor(IPermissionGrantStore grants, IRoleCapabilityStore capabilities)
    : IItemWriteInterceptor
{
    private const string CollectionName = "role";

    public bool AppliesTo(string collection) =>
        string.Equals(collection, CollectionName, StringComparison.OrdinalIgnoreCase);

    public async Task InTransactionAsync(ItemWriteContext context, CancellationToken ct)
    {
        if (context.Operation != ItemWriteOperation.Purge || !Guid.TryParse(context.Id, out var roleId)) return;

        await grants.DeleteForRoleAsync(roleId, ct);
        await capabilities.DeleteForRoleAsync(roleId, ct);
    }
}
