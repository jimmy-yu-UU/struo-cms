// src/Struo.Application/Query/Write/RoleWriteInterceptor.cs
using Struo.Application.Metadata;
using Struo.Application.Security;
using Struo.Domain.Query;

namespace Struo.Application.Query;

/// <summary>
/// The <c>role</c> collection's write rules. A super-admin role (<c>IsSuperAdmin</c>, a read-only field
/// that only migrations and seeds set) and the <c>public</c> role can be changed only by a super-admin and can
/// never be deleted; only a super-admin can create or rename a role to the name of the <c>public</c> role.
/// A purge removes the role's permission rows in the same transaction as the role row; its user-role
/// junction rows are already removed by the purge pipeline, as the target side of <c>User.Roles</c>.
/// Resolution reads only existing roles, so no cached state names a purged role.
/// </summary>
public sealed class RoleWriteInterceptor(
    IPermissionGrantStore grants, SuperAdminGuard guard, IEntityRegistry registry) : IItemWriteInterceptor
{
    private const string NameProperty = "Name";

    public bool AppliesTo(string collection) =>
        string.Equals(collection, RbacCollections.Role, StringComparison.OrdinalIgnoreCase);

    public async Task BeforeWriteAsync(ItemWriteContext context, CancellationToken ct)
    {
        if (context.Operation == ItemWriteOperation.Create)
        {
            guard.RequireRoleNameAllowed(NameOf(context, context.Entity));
            return;
        }
        if (!Guid.TryParse(context.Id, out var roleId)) return;

        await guard.RequireToChangeRolesAsync([roleId], grants: false, ct);
        if (context.Operation is ItemWriteOperation.Update or ItemWriteOperation.Revert)
        {
            var name = NameOf(context, context.Entity);
            if (name != NameOf(context, context.Existing)) guard.RequireRoleNameAllowed(name);
        }

        if (context.Operation is not (ItemWriteOperation.Trash or ItemWriteOperation.Purge)) return;
        if (await guard.IsSuperAdminRoleAsync(roleId, ct))
            throw new ConflictException("A super-admin role cannot be deleted.");
        if (await guard.IsPublicRoleAsync(roleId, ct))
            throw new ConflictException("The public role cannot be deleted.");
    }

    // The role's name after the body is merged (the entity), or before it (the pre-merge copy).
    private string? NameOf(ItemWriteContext context, object? row) =>
        row is null ? null : registry.Get(context.Meta.Name)!.Properties.GetValueOrDefault(NameProperty)?.GetValue(row) as string;

    public async Task InTransactionAsync(ItemWriteContext context, CancellationToken ct)
    {
        if (context.Operation != ItemWriteOperation.Purge || !Guid.TryParse(context.Id, out var roleId)) return;

        await grants.DeleteForRoleAsync(roleId, ct);
    }
}
