// src/Struo.Api/Auth/PermissionResolutionMiddleware.cs
using Struo.Application.Abstractions;
using Struo.Application.Security;

namespace Struo.Api.Auth;

/// <summary>Resolves the caller's effective permissions ONCE per request (after authentication)
/// into the scoped <see cref="ICurrentPermissionsWriter"/> holder. One role/permission resolution per
/// request — how many DB round trips that resolution costs is an implementation detail of
/// <see cref="IRolePermissionStore"/>, not of this middleware.</summary>
public sealed class PermissionResolutionMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(
        HttpContext context,
        ICurrentUserAccessor currentUser,
        IRolePermissionStore store,
        ICurrentPermissionsWriter current)
    {
        await ResolveAndSetAsync(currentUser.GetCurrentUserId(), store, current, context.RequestAborted);
        await next(context);
    }

    /// <summary>Loads the caller's raw role/permission rows, folds them into an effective-permissions
    /// snapshot, and publishes it into the scoped <see cref="ICurrentPermissionsWriter"/> holder.</summary>
    private static async Task ResolveAndSetAsync(
        Guid? userId, IRolePermissionStore store, ICurrentPermissionsWriter current, CancellationToken ct)
    {
        var data = await store.LoadForUserAsync(userId, ct);
        current.Set(PermissionResolver.Resolve(data));
    }
}
