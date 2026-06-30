namespace Struo.Application.Security;

public sealed record RoleRow(Guid Id, string Name, bool IsSuperAdmin);

public sealed record PermissionRow(
    Guid RoleId, string Collection, bool CanRead, bool CanWrite, bool CanDelete);

public sealed record RolePermissionData(
    IReadOnlyList<RoleRow> Roles, IReadOnlyList<PermissionRow> Permissions);

/// <summary>
/// Loads the RBAC data for a caller, querying the DB <b>directly</b> and bypassing the generic
/// projection / permission gating — the query that resolves the gate must never itself be gated
/// (mirrors <see cref="IUserCredentialStore"/>). <paramref name="userId"/> null = anonymous, which
/// loads the <c>public</c> role's grants.
/// </summary>
public interface IRolePermissionStore
{
    Task<RolePermissionData> LoadForUserAsync(Guid? userId, CancellationToken ct = default);
}
