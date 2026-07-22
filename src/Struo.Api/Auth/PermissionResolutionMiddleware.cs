// src/Struo.Api/Auth/PermissionResolutionMiddleware.cs
using Struo.Application.Abstractions;
using Struo.Application.Security;

namespace Struo.Api.Auth;

/// <summary>Resolves the caller's effective permissions ONCE per request (after authentication)
/// into the scoped <see cref="ICurrentPermissions"/> snapshot. One DB load per request.</summary>
public sealed class PermissionResolutionMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(
        HttpContext context,
        ICurrentUserAccessor currentUser,
        IRolePermissionStore store,
        ICurrentPermissions current)
    {
        await ResolveAndSetAsync(currentUser.GetCurrentUserId(), store, current, context.RequestAborted);
        await next(context);
    }

    /// <summary>
    /// BL-1: the load+resolve+set sequence shared with <see cref="FileAccessPolicy"/>'s bearer-adopt
    /// path — both need to (re)compute an effective-permissions snapshot for a given user id and
    /// publish it into the scoped <see cref="ICurrentPermissions"/> holder. Kept here, alongside the
    /// middleware that owns the per-request cookie-path resolution, so the sequence is defined in
    /// exactly one place instead of being duplicated.
    /// </summary>
    internal static async Task ResolveAndSetAsync(
        Guid? userId, IRolePermissionStore store, ICurrentPermissions current, CancellationToken ct)
    {
        var data = await store.LoadForUserAsync(userId, ct);
        current.Set(PermissionResolver.Resolve(data));
    }
}
