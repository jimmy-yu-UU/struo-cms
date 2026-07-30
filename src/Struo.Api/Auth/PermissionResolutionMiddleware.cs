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

    /// <summary>Loads the caller's raw role/permission rows, folds them into an effective-permissions
    /// snapshot, and publishes it into the scoped <see cref="ICurrentPermissions"/> holder.</summary>
    private static async Task ResolveAndSetAsync(
        Guid? userId, IRolePermissionStore store, ICurrentPermissions current, CancellationToken ct)
    {
        var data = await store.LoadForUserAsync(userId, ct);
        current.Set(PermissionResolver.Resolve(data));
    }
}
