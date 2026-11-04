namespace Struo.Application.Security;

/// <summary>Administration of the capabilities a role holds. The read side that resolves a caller's
/// capabilities on every request is <see cref="IRolePermissionStore"/>.</summary>
public interface IRoleCapabilityStore
{
    /// <summary>Is this the <c>public</c> role, which every caller holds and which therefore cannot hold a capability?</summary>
    Task<bool> IsPublicRoleAsync(Guid roleId, CancellationToken ct = default);

    /// <summary>The role's capability codes, in no particular order.</summary>
    Task<IReadOnlyList<string>> ListAsync(Guid roleId, CancellationToken ct = default);

    /// <summary>Replaces the role's whole capability set in one transaction, joining the caller's
    /// transaction when one is open. The caller has validated the codes and removed repeats.</summary>
    Task ReplaceAsync(Guid roleId, IReadOnlyList<string> codes, CancellationToken ct = default);
}
