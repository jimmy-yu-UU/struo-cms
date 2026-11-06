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

    /// <summary>Refuses (403) a caller who is not a super-admin when any of <paramref name="roleIds"/> is a
    /// super-admin role or the <c>public</c> role. <paramref name="grants"/> says whether the change is to the
    /// role's permission grants rather than to the role itself.</summary>
    public async Task RequireToChangeRolesAsync(
        IReadOnlyCollection<Guid> roleIds, bool grants, CancellationToken ct = default)
    {
        if (CallerIsSuperAdmin || roleIds.Count == 0) return;
        var what = grants ? "the grants of " : "";
        if ((await directory.SuperAdminRoleIdsAsync(roleIds, ct)).Count > 0)
            throw Refusal($"change {what}a super-admin role");
        if ((await directory.PublicRoleIdsAsync(roleIds, ct)).Count > 0)
            throw Refusal($"change {what}the public role");
    }

    /// <summary>Whether <paramref name="roleId"/> is the <c>public</c> role.</summary>
    public async Task<bool> IsPublicRoleAsync(Guid roleId, CancellationToken ct = default) =>
        (await directory.PublicRoleIdsAsync([roleId], ct)).Contains(roleId);

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

    /// <summary>Takes a row lock on every super-admin role until the transaction ends. A write that reads
    /// super-admin membership calls it first, so that concurrent writes of that kind run one after the other
    /// and each reads what the previous one committed.</summary>
    public Task LockSuperAdminRolesAsync(CancellationToken ct = default) => directory.LockSuperAdminRolesAsync(ct);

    /// <summary>The accounts among <paramref name="emails"/> that belong to a super-admin role.</summary>
    public Task<IReadOnlySet<string>> SuperAdminEmailsAsync(
        IReadOnlyCollection<string> emails, CancellationToken ct = default) =>
        directory.SuperAdminEmailsAsync(emails, ct);

    /// <summary>Whether <paramref name="userId"/> is an active member of a super-admin role.</summary>
    public async Task<bool> IsActiveSuperAdminAsync(Guid userId, CancellationToken ct = default) =>
        (await directory.ActiveSuperAdminUserIdsAsync(ct)).Contains(userId);

    /// <summary>The users among <paramref name="userIds"/> who belong to a super-admin role, for a caller who
    /// is not a super-admin; empty for a super-admin, who needs no post-condition.</summary>
    public async Task<IReadOnlySet<Guid>> SuperAdminStatusBeforeAsync(
        IReadOnlyCollection<Guid> userIds, CancellationToken ct = default) =>
        CallerIsSuperAdmin || userIds.Count == 0
            ? new HashSet<Guid>()
            : await directory.SuperAdminUserIdsAsync(userIds, ct);

    /// <summary>Throws (403) when a caller who is not a super-admin changed whether any of
    /// <paramref name="userIds"/> belongs to a super-admin role, whatever the request body looked like. Called
    /// inside the write transaction after the relations are synchronized, so the write rolls back.</summary>
    public async Task EnsureSuperAdminStatusUnchangedAsync(
        IReadOnlySet<Guid> before, IReadOnlyCollection<Guid> userIds, CancellationToken ct = default)
    {
        if (CallerIsSuperAdmin || userIds.Count == 0) return;
        var after = await directory.SuperAdminUserIdsAsync(userIds, ct);
        if (!after.SetEquals(before)) throw Refusal("assign or remove a super-admin role");
    }

    /// <summary>Throws a conflict (409) when no active account belongs to a super-admin role. Called after
    /// a write that could have removed the last one, inside its transaction, so the write rolls back.</summary>
    public async Task EnsureActiveSuperAdminAsync(CancellationToken ct = default)
    {
        // The role rows are locked first, so a concurrent transaction that also removes a super-admin waits
        // here until this one commits; the count that follows is a locking read of the latest rows.
        await directory.LockSuperAdminRolesAsync(ct);
        if ((await directory.ActiveSuperAdminUserIdsAsync(ct)).Count == 0)
            throw new ConflictException(
                "At least one active account must remain in a super-admin role.");
    }

    private static PermissionDeniedException Refusal(string action) =>
        new($"Only a super-admin can {action}.");
}
