// src/Struo.Api/Auth/FileAccessPolicy.cs
using Microsoft.AspNetCore.Authentication;
using Struo.Application.Abstractions;
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
    private const string FileCollection = "file";

    public bool CanWrite() => permissions.CanWrite(FileCollection);

    public bool CanDelete() => permissions.CanDelete(FileCollection);

    /// <summary>
    /// SEC-5: may the current caller read a NON-published file? Requires both (a) an authenticated
    /// identity and (b) a genuine <c>CanRead("file")</c> grant for that identity.
    /// </summary>
    /// <remarks>
    /// FilesController's Get/Download carry no <c>[Authorize]</c> so anonymous callers can still
    /// fetch published files (public image serving). Two consequences that this method handles:
    /// <list type="bullet">
    /// <item>Auth is probed, not enforced. <see cref="AuthSchemes.CookieOrBearer"/> is a comma-joined
    /// pair meant for <c>[Authorize]</c>'s multi-scheme syntax, not a single registered scheme, so it
    /// can't be handed to <c>AuthenticateAsync</c>; each real scheme is probed independently.</item>
    /// <item>Because there is no <c>[Authorize]</c>, <c>UseAuthentication</c> only ran the DEFAULT
    /// (cookie) scheme, so <c>PermissionResolutionMiddleware</c> resolved the per-request permission
    /// snapshot from the cookie identity — or from anonymous/public for a bearer-only caller. For the
    /// bearer path we therefore adopt the token's principal and re-resolve its real grants, so
    /// <c>CanRead("file")</c> reflects the actual caller rather than the public floor (which, when
    /// "file" is a public-read collection, would otherwise let any bearer token read drafts).</item>
    /// </list>
    /// The authenticated pre-condition is kept deliberately: with "file" as a public-read collection an
    /// anonymous caller's <c>CanRead("file")</c> is true, so gating on <c>CanRead</c> alone would be
    /// weaker than the status quo for anonymous callers.
    /// </remarks>
    public async Task<bool> CanReadUnpublishedAsync(HttpContext httpContext, CancellationToken ct)
    {
        // Fast path: a cookie identity was already authenticated by UseAuthentication and its grants
        // are in the per-request snapshot resolved by PermissionResolutionMiddleware.
        if (currentUser.GetCurrentUserId() is not null)
            return permissions.CanRead(FileCollection);

        // Bearer-only caller: probe the bearer scheme explicitly (no [Authorize] means it was never
        // run), adopt its principal, and resolve THAT user's real per-collection grants. The
        // load+resolve+set sequence itself lives in PermissionResolutionMiddleware so it isn't
        // duplicated between the middleware's cookie path and this bearer-adopt path (BL-1).
        var bearer = await httpContext.AuthenticateAsync(AuthSchemes.Bearer);
        if (!bearer.Succeeded || bearer.Principal is null) return false; // anonymous: deny unpublished
        // BL-1 invariant: caller MUST pass the request's ambient HttpContext. The line below only
        // WRITES the adopted principal onto this parameter; the user id is READ back afterward via
        // `currentUser` (IHttpContextAccessor), not from this parameter, so a non-ambient HttpContext
        // here would resolve the wrong (anonymous) user instead of the bearer identity just adopted.
        httpContext.User = bearer.Principal;
        var bearerUserId = currentUser.GetCurrentUserId();
        // BL-2: a bearer principal that authenticated but carries no resolvable NameIdentifier must
        // never fall through to the (still anonymous) public-floor snapshot — deny outright rather
        // than resolving grants for a null user id.
        if (bearerUserId is null) return false;
        await PermissionResolutionMiddleware.ResolveAndSetAsync(
            bearerUserId, permissionStore, currentPermissions, ct);
        return permissions.CanRead(FileCollection);
    }
}
