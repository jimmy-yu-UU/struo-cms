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
        var data = await store.LoadForUserAsync(currentUser.GetCurrentUserId(), context.RequestAborted);
        current.Set(PermissionResolver.Resolve(data));
        await next(context);
    }
}
