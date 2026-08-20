using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;
using Struo.Api.Auth;
using Struo.Api.Http;
using ErrorCodes = Struo.Api.Http.ErrorCodes; // disambiguates from the global `HotChocolate.ErrorCodes` using (GraphQl)
using Struo.Application.Abstractions;
using Struo.Application.Configuration;
using Struo.Application.Metadata;
using Struo.Application.Security;

namespace Struo.Api.Controllers;

public sealed record CreateUserRequest(string Email, string Password, string? Name);
public sealed record ChangePasswordRequest(string NewPassword, string? CurrentPassword);

[ApiController]
[Route("api/users")]
[Authorize(AuthenticationSchemes = AuthSchemes.CookieOrBearer)]
public sealed class UsersController(
    IUserAccountStore accounts, IPasswordHasher hasher, IUserCredentialStore store,
    ICurrentPermissions permissions, ICurrentUserAccessor currentUser,
    IOptions<PasswordPolicyOptions> passwordPolicy) : ControllerBase
{
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
        if (PasswordPolicy.Validate(body.Password, passwordPolicy.Value) is { } policyError)
            return ApiResults.Fail(StatusCodes.Status400BadRequest, ErrorCodes.BadUserInput, policyError);
        if (await store.FindByEmailAsync(body.Email, ct) is not null)
            return ApiResults.Fail(StatusCodes.Status409Conflict, ErrorCodes.Conflict, "Email already in use.");

        var id = await accounts.CreateAsync(body.Email, hasher.Hash(body.Password), body.Name, ct);
        return Created($"/api/items/user/{id}", new { id, email = body.Email, name = body.Name });
    }

    [HttpPut("{id:guid}/password")]
    // Same rationale as the login limiter, one layer in: the self-service branch runs a full Argon2id
    // verify on a caller-supplied value, and being behind authentication puts it outside the login
    // policy entirely. Partitioned per user — see PasswordRateLimitOptions.
    [EnableRateLimiting("password")]
    public async Task<IActionResult> ChangePassword(Guid id, [FromBody] ChangePasswordRequest body, CancellationToken ct)
    {
        if (PasswordPolicy.Validate(body.NewPassword, passwordPolicy.Value) is { } policyError)
            return ApiResults.Fail(StatusCodes.Status400BadRequest, ErrorCodes.BadUserInput, policyError);

        var isSelf = currentUser.GetCurrentUserId() is { } me && me == id;
        if (!isSelf)
        {
            if (RequireAdmin() is { } denied) return denied; // changing another user → admin only
        }
        else
        {
            // Self-service: must prove knowledge of the current password.
            var existing = await store.FindByIdAsync(id, ct);
            if (existing is null) return NotFound();

            // OIDC-provisioned accounts carry an empty hash. Guard BEFORE verifying — handing an
            // empty encoded string to the hasher is unacceptable in either outcome (a throw is a
            // masked 500; a false reads as "wrong current password", which is misleading).
            if (string.IsNullOrEmpty(existing.PasswordEncoded))
                return ApiResults.Fail(StatusCodes.Status400BadRequest, ErrorCodes.NoLocalPassword,
                    "This account signs in through an external provider and has no local password.");

            if (string.IsNullOrEmpty(body.CurrentPassword) || !hasher.Verify(existing.PasswordEncoded, body.CurrentPassword))
                return ApiResults.Fail(StatusCodes.Status400BadRequest, ErrorCodes.InvalidCurrentPassword,
                    "Current password is incorrect.");
        }

        var updated = await accounts.SetPasswordAsync(
            id, hasher.Hash(body.NewPassword), currentUser.GetCurrentUserId(), DateTime.UtcNow, ct);
        return updated ? NoContent() : NotFound();
    }

    [HttpPost("{id:guid}/access-token")]
    public async Task<IActionResult> GenerateToken(Guid id, CancellationToken ct)
    {
        if (RequireAdmin() is { } denied) return denied;
        var (token, hash) = AccessTokenHasher.Generate();
        var updated = await accounts.SetAccessTokenAsync(
            id, hash, currentUser.GetCurrentUserId(), DateTime.UtcNow, ct);
        if (!updated) return NotFound();
        return Ok(new { token }); // shown once
    }

    [HttpDelete("{id:guid}/access-token")]
    public async Task<IActionResult> RevokeToken(Guid id, CancellationToken ct)
    {
        if (RequireAdmin() is { } denied) return denied;
        var updated = await accounts.ClearAccessTokenAsync(
            id, currentUser.GetCurrentUserId(), DateTime.UtcNow, ct);
        return updated ? NoContent() : NotFound();
    }

    /// <summary>
    /// Read-only preview for the User form. Reuses the exact per-request resolution pair
    /// (IRolePermissionStore + PermissionResolver), so the preview is by construction identical to
    /// real authorization — including the public-role floor for every caller and the super-admin
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
        if (!await accounts.ExistsAsync(id, ct))
            return ApiResults.Fail(StatusCodes.Status404NotFound, ErrorCodes.NotFound, "User not found.");

        RolePermissionData data;
        // Read the raw query directly rather than via a bound `string? roles` parameter: ASP.NET
        // Core's SimpleTypeModelBinder binds an empty query VALUE to null for string parameters,
        // which would make "?roles=" indistinguishable from the param being absent entirely — and
        // the whole point of this endpoint is that those two cases mean different things (absent =
        // stored roles, empty = hypothetical public-floor preview).
        if (!Request.Query.TryGetValue("roles", out var rolesValues))
        {
            data = await rolePermissions.LoadForUserAsync(id, ct);
        }
        else
        {
            var roles = rolesValues.ToString();
            // Hypothetical preview of an unsaved role selection. Empty -> public floor.
            var parts = roles.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var ids = new List<Guid>(parts.Length);
            foreach (var p in parts)
            {
                if (!Guid.TryParse(p, out var rid))
                    return ApiResults.Fail(StatusCodes.Status400BadRequest, ErrorCodes.BadUserInput,
                        $"Malformed role id: {p}");
                ids.Add(rid);
            }
            data = await rolePermissions.LoadForRolesAsync(ids, ct);
            // Unknown ids silently shrinking the preview would show grants that don't match the
            // selection — reject instead. Skip when no ids were requested: there is nothing to
            // validate (no id can be "missing" when none was asked for), and the empty request still
            // gets the public floor via LoadForRolesAsync's built-in union.
            if (ids.Count > 0)
            {
                var loaded = data.Roles.Select(r => r.Id).ToHashSet();
                var missing = ids.Where(i => !loaded.Contains(i)).ToList();
                if (missing.Count > 0)
                    return ApiResults.Fail(StatusCodes.Status400BadRequest, ErrorCodes.BadUserInput,
                        $"Unknown role ids: {string.Join(", ", missing)}");
            }
        }
        var eff = PermissionResolver.Resolve(data);
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
