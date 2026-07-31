// src/Struo.Application/Security/IPermissionGrantStore.cs
namespace Struo.Application.Security;

/// <summary>One role-to-collection grant as the admin permission matrix edits it.</summary>
public sealed record PermissionGrant(string Collection, bool CanRead, bool CanWrite, bool CanDelete);

/// <summary>
/// Administration of a role's grant set — the write side of RBAC. Deliberately separate from
/// <see cref="IRolePermissionStore"/>, which exists to RESOLVE a caller's effective permissions on
/// every request and is documented as bypassing permission gating precisely because the query that
/// resolves the gate must never itself be gated. Mixing administrative writes into that interface
/// would blur a boundary worth keeping sharp.
/// </summary>
public interface IPermissionGrantStore
{
    /// <summary>Does this role exist? Used for the 404 that precedes reading or replacing its grants.</summary>
    Task<bool> RoleExistsAsync(Guid roleId, CancellationToken ct = default);

    /// <summary>The role's current grants, in no particular order (the caller sorts for display).</summary>
    Task<IReadOnlyList<PermissionGrant>> ListAsync(Guid roleId, CancellationToken ct = default);

    /// <summary>
    /// Full replacement of the role's grant set, atomically. The matrix always submits everything it
    /// knows, so replace is the natural shape; grants carrying none of the three flags are the
    /// caller's to filter out, since "no flags" means "no grant" and storing such a row would be
    /// indistinguishable from absence.
    /// <para>
    /// Implementations must apply this in ONE transaction: a partial application would leave the role
    /// holding neither its old grant set nor its new one, which for an authorization table means a
    /// silently wrong security posture rather than merely stale data.
    /// </para>
    /// </summary>
    Task ReplaceAsync(Guid roleId, IReadOnlyList<PermissionGrant> grants, CancellationToken ct = default);
}
