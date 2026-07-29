// src/Struo.Api/Controllers/RolesController.cs
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SqlSugar;
using Struo.Api.Auth;
using Struo.Api.Http;
using ErrorCodes = Struo.Api.Http.ErrorCodes; // disambiguates from HotChocolate.ErrorCodes
using Struo.Application.Metadata;
using Struo.Application.Security;
using Struo.Infrastructure.Identity;

namespace Struo.Api.Controllers;

/// <summary>Wire DTO for one grant row of the Role permission matrix.</summary>
public sealed record RolePermissionEntry(string Collection, bool CanRead, bool CanWrite, bool CanDelete);

[ApiController]
[Route("api/roles")]
[Authorize(AuthenticationSchemes = AuthSchemes.CookieOrBearer)]
public sealed class RolesController(
    ISqlSugarClient db, IMetadataProvider metadata, ICurrentPermissions permissions) : ControllerBase
{
    private IActionResult? RequireAdmin() =>
        permissions.Current.IsSuperAdmin
            ? null
            : ApiResults.Fail(StatusCodes.Status403Forbidden, ErrorCodes.Forbidden, "Admin role required.");

    [HttpGet("{id:guid}/permissions")]
    public async Task<IActionResult> GetPermissions(Guid id, CancellationToken ct)
    {
        if (RequireAdmin() is { } denied) return denied;
        if (!await db.Queryable<Role>().Where(r => r.Id == id).AnyAsync(ct))
            return ApiResults.Fail(StatusCodes.Status404NotFound, ErrorCodes.NotFound, "Role not found.");

        var rows = await db.Queryable<Permission>().Where(p => p.RoleId == id).ToListAsync(ct);
        return Ok(rows.Select(ToEntry).OrderBy(e => e.Collection, StringComparer.Ordinal).ToList());
    }

    /// <summary>
    /// Full-replace of the role's grant set (the matrix always PUTs everything it knows).
    /// Implemented as delete-all + insert-all in one transaction — observably identical to
    /// upsert+prune and simpler; Permission rows are hidden implementation detail, so their
    /// identity is not part of any contract. Rows whose three flags are all false carry no grant
    /// and are treated as absent (deleted, never stored).
    /// </summary>
    [HttpPut("{id:guid}/permissions")]
    public async Task<IActionResult> PutPermissions(
        Guid id, [FromBody] List<RolePermissionEntry>? body, CancellationToken ct)
    {
        if (RequireAdmin() is { } denied) return denied;
        if (body is null)
            return ApiResults.Fail(StatusCodes.Status400BadRequest, ErrorCodes.BadUserInput,
                "Body must be a JSON array of permission entries.");
        if (!await db.Queryable<Role>().Where(r => r.Id == id).AnyAsync(ct))
            return ApiResults.Fail(StatusCodes.Status404NotFound, ErrorCodes.NotFound, "Role not found.");

        // Duplicates checked on the raw body (an all-false duplicate still signals a client bug).
        var dupes = body.GroupBy(e => e.Collection, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1).Select(g => g.Key).ToList();
        if (dupes.Count > 0)
            return ApiResults.Fail(StatusCodes.Status400BadRequest, ErrorCodes.BadUserInput,
                $"Duplicate collection entries: {string.Join(", ", dupes)}.");

        var unknown = body.Where(e => string.IsNullOrWhiteSpace(e.Collection)
                                      || metadata.GetCollection(e.Collection) is null)
            .Select(e => e.Collection).Distinct().ToList();
        if (unknown.Count > 0)
            return ApiResults.Fail(StatusCodes.Status400BadRequest, ErrorCodes.BadUserInput,
                $"Unknown collections: {string.Join(", ", unknown)}.");

        var rows = body
            .Where(e => e.CanRead || e.CanWrite || e.CanDelete)
            .Select(e => new Permission
            {
                // Canonicalize casing: the unknown-collection check above guarantees this is non-null.
                Id = Guid.CreateVersion7(), RoleId = id, Collection = metadata.GetCollection(e.Collection)!.Name,
                CanRead = e.CanRead, CanWrite = e.CanWrite, CanDelete = e.CanDelete,
            })
            .ToList();

        // BeginTranAsync has no CancellationToken overload (same note as SqlSugarItemRepository).
        try
        {
            await db.Ado.BeginTranAsync();
            await db.Deleteable<Permission>().Where(p => p.RoleId == id).ExecuteCommandAsync(ct);
            if (rows.Count > 0) await db.Insertable(rows).ExecuteCommandAsync(ct);
            await db.Ado.CommitTranAsync();
        }
        catch
        {
            await db.Ado.RollbackTranAsync();
            throw;
        }

        return Ok(rows.Select(ToEntry).OrderBy(e => e.Collection, StringComparer.Ordinal).ToList());
    }

    private static RolePermissionEntry ToEntry(Permission p) =>
        new(p.Collection, p.CanRead, p.CanWrite, p.CanDelete);
}
