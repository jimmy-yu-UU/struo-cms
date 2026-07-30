// src/Struo.Api/Auth/FileAccessPolicy.cs
using Microsoft.AspNetCore.Authentication;
using Struo.Application.Abstractions;
using Struo.Application.Files;
using Struo.Application.Security;

namespace Struo.Api.Auth;

/// <summary>Default <see cref="IFileAccessPolicy"/>: a thin wrapper over the same RBAC primitives
/// FilesController used to depend on directly. Scoped lifetime (matches <see cref="IPermissionService"/>
/// and <see cref="ICurrentPermissions"/>, both of which are themselves scoped, i.e. one resolution per
/// request).</summary>
public sealed class FileAccessPolicy(
    IPermissionService permissions,
    ICurrentUserAccessor currentUser,
    IRolePermissionStore permissionStore,
    ICurrentPermissions currentPermissions) : IFileAccessPolicy
{
    public bool CanWrite() => permissions.CanWrite(FileCollection.Name);

    public bool CanDelete() => permissions.CanDelete(FileCollection.Name);

    /// <summary>
    /// May the current caller read a NON-published file? Requires both (a) an authenticated identity
    /// and (b) a <c>CanWrite("file")</c> grant.
    /// </summary>
    /// <remarks>
    /// The gate is WRITE, not read, because <c>public</c> is a permission floor for every caller
    /// (<c>SqlSugarRolePermissionStore</c>): when <c>file</c> is a public-read collection — the
    /// documented, expected setup for serving images anonymously — <c>CanRead("file")</c> is true for
    /// anonymous and authenticated callers alike and therefore gates nothing. A draft is an editorial
    /// state, so the caller who may edit files is the caller who may see them. No public role is given
    /// write, so this is immune to the floor.
    /// <para>FilesController's Get/Download carry no <c>[Authorize]</c> so anonymous callers can still
    /// fetch published files; auth is probed, not enforced. <see cref="AuthSchemes.CookieOrBearer"/> is
    /// a comma-joined pair for <c>[Authorize]</c>'s multi-scheme syntax, not a registered scheme, so it
    /// cannot be handed to <c>AuthenticateAsync</c> — each real scheme is probed independently.</para>
    /// </remarks>
    public async Task<bool> CanReadUnpublishedAsync(HttpContext httpContext, CancellationToken ct)
    {
        // Fast path: a cookie identity was already authenticated by UseAuthentication and its grants
        // are in the per-request snapshot resolved by PermissionResolutionMiddleware.
        if (currentUser.GetCurrentUserId() is not null)
            return permissions.CanWrite(FileCollection.Name);

        // Bearer-only caller: probe the bearer scheme explicitly (no [Authorize] means it was never
        // run), adopt its principal, and resolve THAT user's real per-collection grants. The
        // load+resolve+set sequence itself lives in PermissionResolutionMiddleware so it isn't
        // duplicated between the middleware's cookie path and this bearer-adopt path.
        var bearer = await httpContext.AuthenticateAsync(AuthSchemes.Bearer);
        if (!bearer.Succeeded || bearer.Principal is null) return false; // anonymous: deny unpublished
        // Invariant: caller MUST pass the request's ambient HttpContext. The line below only
        // WRITES the adopted principal onto this parameter; the user id is READ back afterward via
        // `currentUser` (IHttpContextAccessor), not from this parameter, so a non-ambient HttpContext
        // here would resolve the wrong (anonymous) user instead of the bearer identity just adopted.
        httpContext.User = bearer.Principal;
        var bearerUserId = currentUser.GetCurrentUserId();
        // A bearer principal that authenticated but carries no resolvable NameIdentifier must
        // never fall through to the (still anonymous) public-floor snapshot — deny outright rather
        // than resolving grants for a null user id.
        if (bearerUserId is null) return false;
        await PermissionResolutionMiddleware.ResolveAndSetAsync(
            bearerUserId, permissionStore, currentPermissions, ct);
        return permissions.CanWrite(FileCollection.Name);
    }
}
