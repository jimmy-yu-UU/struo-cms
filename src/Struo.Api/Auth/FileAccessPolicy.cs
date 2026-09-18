// src/Struo.Api/Auth/FileAccessPolicy.cs
using Struo.Application.Abstractions;
using Struo.Application.Files;
using Struo.Application.Security;
using Struo.Infrastructure.Identity;

namespace Struo.Api.Auth;

/// <summary>Default <see cref="IFileAccessPolicy"/>: a thin wrapper over the RBAC primitives
/// FilesController consumes through this abstraction rather than directly. Scoped lifetime (matches <see cref="IPermissionService"/>
/// and <see cref="ICurrentPermissions"/>, both of which are themselves scoped, i.e. one resolution per
/// request).</summary>
public sealed class FileAccessPolicy(
    IPermissionService permissions,
    ICurrentUserAccessor currentUser) : IFileAccessPolicy
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
    /// documented setup for serving images anonymously — <c>CanRead("file")</c> is true for anonymous
    /// and authenticated callers alike and therefore gates nothing. A draft is an editorial state, so
    /// the caller who may edit files is the caller who may see them. The shipped seed
    /// (<see cref="RbacSeeder.SeedAsync"/>) grants <c>public</c> read only — granting it write as well
    /// would widen this gate to every signed-in caller (an anonymous request still gets a 404).
    /// <para>Both identity kinds are already resolved by the time this runs: the default
    /// <see cref="AuthSchemes.Adaptive"/> policy scheme authenticates cookie AND bearer callers on
    /// every endpoint, including FilesController's Get/Download which carry no <c>[Authorize]</c>, and
    /// <see cref="PermissionResolutionMiddleware"/> has already published that identity's grants into
    /// the scoped snapshot. No scheme probing is needed here.</para>
    /// </remarks>
    public bool CanReadUnpublished() =>
        currentUser.GetCurrentUserId() is not null && permissions.CanWrite(FileCollection.Name);
}
