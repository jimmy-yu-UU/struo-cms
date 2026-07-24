using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SqlSugar;
using Struo.Api.Auth;
using Struo.Api.Http;
using ErrorCodes = Struo.Api.Http.ErrorCodes; // disambiguates from the global `HotChocolate.ErrorCodes` using (GraphQl)
using Struo.Application.Abstractions;
using Struo.Application.Metadata;
using Struo.Application.Security;
using Struo.Infrastructure.Identity;

namespace Struo.Api.Controllers;

public sealed record CreateUserRequest(string Email, string Password, string? Name);
public sealed record ChangePasswordRequest(string NewPassword, string? CurrentPassword);

[ApiController]
[Route("api/users")]
[Authorize(AuthenticationSchemes = AuthSchemes.CookieOrBearer)]
public sealed class UsersController(
    ISqlSugarClient db, IPasswordHasher hasher, IUserCredentialStore store,
    ICurrentPermissions permissions, ICurrentUserAccessor currentUser) : ControllerBase
{
    private const int MinPasswordLength = 8;

    private IActionResult? RequireAdmin() =>
        permissions.Current.IsSuperAdmin
            ? null
            : ApiResults.Fail(StatusCodes.Status403Forbidden, ErrorCodes.Forbidden, "Admin role required.");

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateUserRequest body, CancellationToken ct)
    {
        if (RequireAdmin() is { } denied) return denied;
        if (string.IsNullOrWhiteSpace(body.Email))
            return ApiResults.Fail(StatusCodes.Status400BadRequest, ErrorCodes.BadUserInput, "Email is required.");
        if (body.Password is null || body.Password.Length < MinPasswordLength)
            return ApiResults.Fail(StatusCodes.Status400BadRequest, ErrorCodes.BadUserInput,
                $"Password must be at least {MinPasswordLength} characters.");
        if (await store.FindByEmailAsync(body.Email, ct) is not null)
            return ApiResults.Fail(StatusCodes.Status409Conflict, ErrorCodes.Conflict, "Email already in use.");

        var id = Guid.CreateVersion7();
        await db.Insertable(new User
        {
            Id = id, Email = body.Email, Password = hasher.Hash(body.Password), Name = body.Name, IsActive = true
        }).ExecuteCommandAsync(ct);
        return Created($"/api/items/user/{id}", new { id, email = body.Email, name = body.Name });
    }

    [HttpPut("{id:guid}/password")]
    public async Task<IActionResult> ChangePassword(Guid id, [FromBody] ChangePasswordRequest body, CancellationToken ct)
    {
        if (body.NewPassword is null || body.NewPassword.Length < MinPasswordLength)
            return ApiResults.Fail(StatusCodes.Status400BadRequest, ErrorCodes.BadUserInput,
                $"Password must be at least {MinPasswordLength} characters.");

        var isSelf = currentUser.GetCurrentUserId() is { } me && me == id;
        if (!isSelf)
        {
            if (RequireAdmin() is { } denied) return denied; // changing another user → admin only
        }
        else
        {
            // Self-service: must prove knowledge of the current password.
            var existing = await db.Queryable<User>().Where(u => u.Id == id).FirstAsync(ct);
            if (existing is null) return NotFound();
            if (string.IsNullOrEmpty(body.CurrentPassword) || !hasher.Verify(existing.Password, body.CurrentPassword))
                return ApiResults.Fail(StatusCodes.Status401Unauthorized, ErrorCodes.Unauthorized, "Current password is incorrect.");
        }

        var updated = await db.Updateable<User>()
            .SetColumns(u => u.Password == hasher.Hash(body.NewPassword))
            .Where(u => u.Id == id).ExecuteCommandAsync(ct);
        return updated == 0 ? NotFound() : NoContent();
    }

    [HttpPost("{id:guid}/access-token")]
    public async Task<IActionResult> GenerateToken(Guid id, CancellationToken ct)
    {
        if (RequireAdmin() is { } denied) return denied;
        var (token, hash) = AccessTokenHasher.Generate();
        var now = DateTime.UtcNow;
        var updated = await db.Updateable<User>()
            .SetColumns(u => new User { AccessToken = hash, AccessTokenCreatedAt = now, AccessTokenLastUsedAt = null })
            .Where(u => u.Id == id).ExecuteCommandAsync(ct);
        if (updated == 0) return NotFound();
        return Ok(new { token }); // shown once
    }

    [HttpDelete("{id:guid}/access-token")]
    public async Task<IActionResult> RevokeToken(Guid id, CancellationToken ct)
    {
        if (RequireAdmin() is { } denied) return denied;
        var updated = await db.Updateable<User>()
            .SetColumns(u => u.AccessToken == null).Where(u => u.Id == id).ExecuteCommandAsync(ct);
        return updated == 0 ? NotFound() : NoContent();
    }

    /// <summary>
    /// Read-only preview for the User form (問題 4). Reuses the exact per-request resolution pair
    /// (IRolePermissionStore + PermissionResolver), so the preview is by construction identical to
    /// real authorization — including the public-role floor for role-less users and the super-admin
    /// short-circuit. Projection mirrors AuthController.Me: probe each schema collection.
    /// </summary>
    [HttpGet("{id:guid}/effective-permissions")]
    public async Task<IActionResult> GetEffectivePermissions(
        Guid id,
        [FromServices] IRolePermissionStore rolePermissions,
        [FromServices] SchemaService schema,
        CancellationToken ct)
    {
        if (RequireAdmin() is { } denied) return denied;
        if (!await db.Queryable<User>().Where(u => u.Id == id).AnyAsync(ct))
            return ApiResults.Fail(StatusCodes.Status404NotFound, ErrorCodes.NotFound, "User not found.");

        var eff = PermissionResolver.Resolve(await rolePermissions.LoadForUserAsync(id, ct));
        var map = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
        if (!eff.IsSuperAdmin)
        {
            foreach (var c in schema.GetAll())
            {
                var read = eff.CanRead(c.Name);
                var write = eff.CanWrite(c.Name);
                var del = eff.CanDelete(c.Name);
                if (read || write || del)
                    map[c.Name] = new { read, write, @delete = del };
            }
        }
        return Ok(new { isSuperAdmin = eff.IsSuperAdmin, permissions = map });
    }
}
