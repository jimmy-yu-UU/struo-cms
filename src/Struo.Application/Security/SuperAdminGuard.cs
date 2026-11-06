using Struo.Domain.Query;

namespace Struo.Application.Security;

/// <summary>
/// The one place that decides what a caller who is not a super-admin may do to a super-admin account or
/// role, and that keeps at least one active super-admin. REST, GraphQL, the item write path and the
/// dedicated controllers all call it. A super-admin passes every authorization check without a query;
/// <see cref="EnsureActiveSuperAdminAsync"/> binds every caller.
/// </summary>
public sealed class SuperAdminGuard(IPermissionService permissions, ISuperAdminDirectory directory)
{
    public bool CallerIsSuperAdmin => permissions.IsSuperAdmin;

    /// <summary>Refuses (403) a caller who is not a super-admin when <paramref name="userId"/> belongs to a
    /// super-admin role. <paramref name="action"/> completes "Only a super-admin can ...", for example "change a super-admin account".</summary>
    public async Task RequireForUserAsync(Guid userId, string action, CancellationToken ct = default)
    {
        if (CallerIsSuperAdmin) return;
        if ((await directory.SuperAdminUserIdsAsync([userId], ct)).Contains(userId))
            throw Refusal(action);
    }

    /// <summary>Refuses (403) a caller who is not a super-admin when <paramref name="roleId"/> is a
    /// super-admin role.</summary>
    public async Task RequireForRoleAsync(Guid roleId, string action, CancellationToken ct = default) =>
        await RequireForRolesAsync([roleId], action, ct);

    /// <summary>Refuses (403) a caller who is not a super-admin when any of <paramref name="roleIds"/> is a
    /// super-admin role.</summary>
    public async Task RequireForRolesAsync(
        IReadOnlyCollection<Guid> roleIds, string action, CancellationToken ct = default)
    {
        if (CallerIsSuperAdmin || roleIds.Count == 0) return;
        if ((await directory.SuperAdminRoleIdsAsync(roleIds, ct)).Count > 0)
            throw Refusal(action);
    }

    /// <summary>Whether <paramref name="roleId"/> is a super-admin role.</summary>
    public async Task<bool> IsSuperAdminRoleAsync(Guid roleId, CancellationToken ct = default) =>
        (await directory.SuperAdminRoleIdsAsync([roleId], ct)).Contains(roleId);

    /// <summary>The accounts among <paramref name="emails"/> that belong to a super-admin role.</summary>
    public Task<IReadOnlySet<string>> SuperAdminEmailsAsync(
        IReadOnlyCollection<string> emails, CancellationToken ct = default) =>
        directory.SuperAdminEmailsAsync(emails, ct);

    /// <summary>Whether <paramref name="userId"/> is an active member of a super-admin role.</summary>
    public async Task<bool> IsActiveSuperAdminAsync(Guid userId, CancellationToken ct = default) =>
        (await directory.ActiveSuperAdminUserIdsAsync(ct)).Contains(userId);

    /// <summary>Throws a conflict (409) when no active account belongs to a super-admin role. Called after
    /// a write that could have removed the last one, inside its transaction, so the write rolls back.</summary>
    public async Task EnsureActiveSuperAdminAsync(CancellationToken ct = default)
    {
        if ((await directory.ActiveSuperAdminUserIdsAsync(ct)).Count == 0)
            throw new ConflictException(
                "At least one active account must remain in a super-admin role.");
    }

    private static PermissionDeniedException Refusal(string action) =>
        new($"Only a super-admin can {action}.");
}
