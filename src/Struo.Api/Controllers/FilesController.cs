using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Struo.Api.Auth;
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
    FileService files, IFileStorage storage, FileStorageOptions options, IPermissionService permissions) : ControllerBase
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
            return BadRequest(new { error = new { message = "Expected multipart/form-data." } });
        var form = await Request.ReadFormAsync(ct);
        var file = form.Files.GetFile("file");
        if (file is null) return BadRequest(new { error = new { message = "Missing 'file' part." } });

        await using var stream = file.OpenReadStream();
        var created = await files.UploadAsync(stream, file.FileName, file.ContentType, file.Length, ct);
        return StatusCode(StatusCodes.Status201Created, new
        {
            data = new
            {
                id = created.Id, fileName = created.FileName, contentType = created.ContentType,
                size = created.Size, width = created.Width, height = created.Height, status = created.Status
            }
        });
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct)
    {
        var row = await files.GetAsync(id, ct);
        if (row is null) return NotFound();
        if (row.Status != "published" && !await IsAuthenticatedAsync()) return NotFound();
        return Ok(new
        {
            data = new
            {
                id = row.Id, fileName = row.FileName, contentType = row.ContentType,
                size = row.Size, width = row.Width, height = row.Height, status = row.Status
            }
        });
    }

    [HttpGet("{id:guid}/content")]
    public async Task<IActionResult> Download(Guid id, CancellationToken ct)
    {
        var row = await files.GetAsync(id, ct);
        if (row is null) return NotFound();
        if (row.Status != "published" && !await IsAuthenticatedAsync()) return NotFound();

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
    /// Get/Download carry no [Authorize] so anonymous callers can fetch published files (e.g. public
    /// image serving). To let authenticated callers additionally see non-published files without
    /// blocking anonymous access, auth is probed rather than enforced: AuthSchemes.CookieOrBearer is
    /// a comma-joined pair of scheme names meant for [Authorize]'s multi-scheme syntax, not a single
    /// registered scheme, so it can't be passed to AuthenticateAsync directly. Instead each real
    /// scheme is authenticated independently; both handlers return a non-throwing NoResult/Fail when
    /// their credential is absent or invalid.
    /// </summary>
    private async Task<bool> IsAuthenticatedAsync()
    {
        var cookie = await HttpContext.AuthenticateAsync(AuthSchemes.Cookie);
        if (cookie.Succeeded) return true;
        var bearer = await HttpContext.AuthenticateAsync(AuthSchemes.Bearer);
        return bearer.Succeeded;
    }
}
