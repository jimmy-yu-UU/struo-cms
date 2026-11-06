namespace Struo.Application.Security;

/// <summary>
/// Who counts as a super-admin, read straight from the tables and never gated (like
/// <see cref="IRolePermissionStore"/>, it backs an authorization decision). A super-admin role is a
/// <c>role</c> row with <c>IsSuperAdmin</c> set, and a super-admin account is a <c>user</c> that is a
/// member of one. Every method answers for a whole set in one query.
/// </summary>
public interface ISuperAdminDirectory
{
    /// <summary>The ids among <paramref name="userIds"/> that belong to at least one super-admin role.</summary>
    Task<IReadOnlySet<Guid>> SuperAdminUserIdsAsync(IReadOnlyCollection<Guid> userIds, CancellationToken ct = default);

    /// <summary>The emails among <paramref name="emails"/> (already normalized) of accounts that belong to
    /// at least one super-admin role.</summary>
    Task<IReadOnlySet<string>> SuperAdminEmailsAsync(IReadOnlyCollection<string> emails, CancellationToken ct = default);

    /// <summary>The ids among <paramref name="roleIds"/> of roles flagged <c>IsSuperAdmin</c>.</summary>
    Task<IReadOnlySet<Guid>> SuperAdminRoleIdsAsync(IReadOnlyCollection<Guid> roleIds, CancellationToken ct = default);

    /// <summary>The ids among <paramref name="roleIds"/> of the seeded <c>public</c> role, the permission floor
    /// every caller holds; found by the name <c>IRolePermissionStore</c> resolves it by.</summary>
    Task<IReadOnlySet<Guid>> PublicRoleIdsAsync(IReadOnlyCollection<Guid> roleIds, CancellationToken ct = default);

    /// <summary>Takes a row lock on every super-admin role until the surrounding transaction ends, so two
    /// transactions that each count the active members before changing them run one after the other.</summary>
    Task LockSuperAdminRolesAsync(CancellationToken ct = default);

    /// <summary>The ids of the active accounts that belong to at least one super-admin role.</summary>
    Task<IReadOnlySet<Guid>> ActiveSuperAdminUserIdsAsync(CancellationToken ct = default);
}
