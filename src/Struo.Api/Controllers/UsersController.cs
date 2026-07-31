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

    /// <summary>
    /// Chains the audit trio (<c>UpdatedAt</c>/<c>UpdatedBy</c>/<c>Version + 1</c>) onto a
    /// credential-column UPDATE. The credential endpoints below write via SqlSugar's
    /// column-expression form, which is NOT <c>UpdateByObject</c> — so
    /// <c>AuditAop</c> (hooked only on <c>InsertByObject</c>/<c>UpdateByObject</c>) never fires for
    /// them, and they bypass <c>SqlSugarItemRepository.UpdateGenericAsync</c>'s version bump as well.
    /// Without this, a password change left no record of who made it and did not advance the
    /// optimistic-lock token, so a client holding a pre-change <c>version</c> could still write
    /// successfully instead of getting the 409 every other write path produces.
    /// <para>
    /// Uses the <c>SetColumns(u =&gt; new User{...})</c> member-init overload, not the
    /// <c>u =&gt; u.Col == value</c> equality overload, for the same reason
    /// <c>SqlSugarItemRepository.SoftDeleteGenericAsync</c> does: a null <c>UpdatedBy</c> is typed
    /// from the underlying CLR type (Guid) instead of being sent as an untyped null, which PostgreSQL
    /// rejects against a <c>uuid</c> column (42804). <c>Version = u.Version + 1</c> resolves to the
    /// SQL fragment <c>version = version + 1</c> — increment-only, no CAS: these endpoints take no
    /// client version, so they must never fail on one.
    /// </para>
    /// Guarded by <c>UserCredentialWriteAuditTests</c>.
    /// </summary>
    private IUpdateable<User> WithCredentialAudit(IUpdateable<User> updateable)
    {
        var now = DateTime.UtcNow;
        var actor = currentUser.GetCurrentUserId();
        return updateable.SetColumns(u => new User
        {
            UpdatedAt = now,
            UpdatedBy = actor,
            Version = u.Version + 1,
        });
    }

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

        // Hashed once, here, rather than left inside the SetColumns expression for SqlSugar's
        // resolver to evaluate.
        var newHash = hasher.Hash(body.NewPassword);
        var updated = await WithCredentialAudit(db.Updateable<User>()
                .SetColumns(u => new User { Password = newHash }))
            .Where(u => u.Id == id).ExecuteCommandAsync(ct);
        return updated == 0 ? NotFound() : NoContent();
    }

    [HttpPost("{id:guid}/access-token")]
    public async Task<IActionResult> GenerateToken(Guid id, CancellationToken ct)
    {
        if (RequireAdmin() is { } denied) return denied;
        var (token, hash) = AccessTokenHasher.Generate();
        var now = DateTime.UtcNow;
        var updated = await WithCredentialAudit(db.Updateable<User>()
                .SetColumns(u => new User { AccessToken = hash, AccessTokenCreatedAt = now, AccessTokenLastUsedAt = null }))
            .Where(u => u.Id == id).ExecuteCommandAsync(ct);
        if (updated == 0) return NotFound();
        return Ok(new { token }); // shown once
    }

    [HttpDelete("{id:guid}/access-token")]
    public async Task<IActionResult> RevokeToken(Guid id, CancellationToken ct)
    {
        if (RequireAdmin() is { } denied) return denied;
        var updated = await WithCredentialAudit(db.Updateable<User>()
                .SetColumns(u => new User { AccessToken = null }))
            .Where(u => u.Id == id).ExecuteCommandAsync(ct);
        return updated == 0 ? NotFound() : NoContent();
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
        if (!await db.Queryable<User>().Where(u => u.Id == id).AnyAsync(ct))
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
