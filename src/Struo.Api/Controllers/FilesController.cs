using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Struo.Api.Auth;
using Struo.Api.Http;
using ErrorCodes = Struo.Api.Http.ErrorCodes; // disambiguates from the global `HotChocolate.ErrorCodes` using (GraphQl)
using Struo.Application.Abstractions;
using Struo.Application.Files;
using Struo.Application.Security;
using Struo.Domain.Query;
// Alias to avoid importing the Struo.Infrastructure.Files namespace, whose `File` type would
// clash with ControllerBase.File(...) used by the download action.
using FileService = Struo.Infrastructure.Files.FileService;

namespace Struo.Api.Controllers;

[ApiController]
[Route("api/files")]
public sealed class FilesController(
    FileService files, IFileStorage storage, FileStorageOptions options, IPermissionService permissions,
    ICurrentUserAccessor currentUser, IRolePermissionStore permissionStore, ICurrentPermissions currentPermissions)
    : ControllerBase
{
    // The media library is the "file" collection. Mutations go through the dedicated file storage
    // pipeline rather than the generic ItemService, so RBAC must be enforced here too — otherwise any
    // authenticated caller (incl. a role-less SSO user) could upload or delete any file, bypassing the
    // per-collection grants that govern every other collection.
    private const string FileCollection = "file";

    [HttpPost]
    [Authorize(AuthenticationSchemes = AuthSchemes.CookieOrBearer)]
    public async Task<IActionResult> Upload(CancellationToken ct)
    {
        if (!permissions.CanWrite(FileCollection))
            throw new PermissionDeniedException("Write not permitted.");
        if (!Request.HasFormContentType)
            return ApiResults.Fail(StatusCodes.Status400BadRequest, ErrorCodes.BadUserInput, "Expected multipart/form-data.");
        var form = await Request.ReadFormAsync(ct);
        var file = form.Files.GetFile("file");
        if (file is null) return ApiResults.Fail(StatusCodes.Status400BadRequest, ErrorCodes.BadUserInput, "Missing 'file' part.");

        await using var stream = file.OpenReadStream();
        var created = await files.UploadAsync(stream, file.FileName, file.ContentType, file.Length, ct);
        return StatusCode(StatusCodes.Status201Created, new
        {
            id = created.Id, fileName = created.FileName, contentType = created.ContentType,
            size = created.Size, width = created.Width, height = created.Height, status = created.Status
        });
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct)
    {
        var row = await files.GetAsync(id, ct);
        if (row is null) return NotFound();
        // SEC-5: a non-published file requires a genuine per-collection read grant, not merely a
        // logged-in session (a role-less JIT/SSO user could otherwise fetch any draft). 404 (not 403)
        // so existence isn't leaked.
        if (row.Status != "published" && !await CanReadUnpublishedAsync(ct)) return NotFound();
        return Ok(new
        {
            id = row.Id, fileName = row.FileName, contentType = row.ContentType,
            size = row.Size, width = row.Width, height = row.Height, status = row.Status
        });
    }

    [HttpGet("{id:guid}/content")]
    public async Task<IActionResult> Download(Guid id, CancellationToken ct)
    {
        var row = await files.GetAsync(id, ct);
        if (row is null) return NotFound();
        // SEC-5 (see Get above): non-published content is gated on CanRead("file"). 404 so existence
        // isn't leaked.
        if (row.Status != "published" && !await CanReadUnpublishedAsync(ct)) return NotFound();

        var presigned = await storage.GetPresignedUrlAsync(
            row.StorageKey, TimeSpan.FromSeconds(options.S3.PresignTtlSeconds), ct);
        if (presigned is not null) return Redirect(presigned);   // 302 (S3/MinIO)

        var stream = await storage.OpenReadAsync(row.StorageKey, ct);
        return File(stream, row.ContentType, fileDownloadName: row.FileName);
    }

    [HttpDelete("{id:guid}")]
    [Authorize(AuthenticationSchemes = AuthSchemes.CookieOrBearer)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        if (!permissions.CanDelete(FileCollection))
            throw new PermissionDeniedException("Delete not permitted.");
        return await files.DeleteAsync(id, ct) ? NoContent() : NotFound();
    }

    /// <summary>
    /// SEC-5: may the current caller read a NON-published file? Requires both (a) an authenticated
    /// identity and (b) a genuine <c>CanRead("file")</c> grant for that identity.
    /// </summary>
    /// <remarks>
    /// Get/Download carry no <c>[Authorize]</c> so anonymous callers can still fetch published files
    /// (public image serving). Two consequences that this method handles:
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
    private async Task<bool> CanReadUnpublishedAsync(CancellationToken ct)
    {
        // Fast path: a cookie identity was already authenticated by UseAuthentication and its grants
        // are in the per-request snapshot resolved by PermissionResolutionMiddleware.
        if (currentUser.GetCurrentUserId() is not null)
            return permissions.CanRead(FileCollection);

        // Bearer-only caller: probe the bearer scheme explicitly (no [Authorize] means it was never
        // run), adopt its principal, and resolve THAT user's real per-collection grants.
        var bearer = await HttpContext.AuthenticateAsync(AuthSchemes.Bearer);
        if (!bearer.Succeeded || bearer.Principal is null) return false; // anonymous: deny unpublished
        HttpContext.User = bearer.Principal;
        var data = await permissionStore.LoadForUserAsync(currentUser.GetCurrentUserId(), ct);
        currentPermissions.Set(PermissionResolver.Resolve(data));
        return permissions.CanRead(FileCollection);
    }
}
