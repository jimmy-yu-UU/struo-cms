namespace Struo.Application.Security;

public sealed record RoleRow(Guid Id, string Name, bool IsSuperAdmin);

public sealed record PermissionRow(
    Guid RoleId, string Collection, bool CanRead, bool CanWrite, bool CanDelete);

public sealed record RolePermissionData(
    IReadOnlyList<RoleRow> Roles, IReadOnlyList<PermissionRow> Permissions);

/// <summary>
/// Loads the RBAC data for a caller, querying the DB <b>directly</b> and bypassing the generic
/// projection / permission gating — the query that resolves the gate must never itself be gated
/// (mirrors <see cref="IUserCredentialStore"/>). The <c>public</c> role is a permission FLOOR: its
/// grants are unioned into the result for every caller — anonymous (<paramref name="userId"/> is
/// null), role-less, and role-holding alike — so a caller's own roles can only ADD to what
/// <c>public</c> already exposes, never subtract from it.
/// </summary>
public interface IRolePermissionStore
{
    Task<RolePermissionData> LoadForUserAsync(Guid? userId, CancellationToken ct = default);

    /// <summary>
    /// Loads RBAC data for a HYPOTHETICAL role set (User-form preview of an unsaved TagSelect
    /// selection). Unions the same <c>public</c>-role floor as <see cref="LoadForUserAsync"/>: the
    /// result always includes what <c>public</c> grants, whether the list is empty or names specific
    /// roles.
    /// Ids not matching an existing role are simply absent from the result — the caller decides
    /// whether that is an error.
    /// </summary>
    Task<RolePermissionData> LoadForRolesAsync(IReadOnlyList<Guid> roleIds, CancellationToken ct = default);
}
