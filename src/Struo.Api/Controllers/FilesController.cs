using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Struo.Api.Auth;
using Struo.Api.Http;
using ErrorCodes = Struo.Api.Http.ErrorCodes; // disambiguates from the global `HotChocolate.ErrorCodes` using (GraphQl)
using Struo.Application.Files;
using Struo.Domain.Query;
// Alias to avoid importing the Struo.Infrastructure.Files namespace, whose `File` type would
// clash with ControllerBase.File(...) used by the download action.
using FileService = Struo.Infrastructure.Files.FileService;

namespace Struo.Api.Controllers;

[ApiController]
[Route("api/files")]
public sealed class FilesController(
    FileService files, IFileStorage storage, FileStorageOptions options, IFileAccessPolicy access)
    : ControllerBase
{
    // The media library is the "file" collection. Mutations go through the dedicated file storage
    // pipeline rather than the generic ItemService, so RBAC must be enforced here too — otherwise any
    // authenticated caller (incl. a role-less SSO user) could upload or delete any file, bypassing the
    // per-collection grants that govern every other collection. That RBAC policy lives in
    // IFileAccessPolicy (BL-1) so it isn't duplicated with PermissionResolutionMiddleware.
    [HttpPost]
    [Authorize(AuthenticationSchemes = AuthSchemes.CookieOrBearer)]
    public async Task<IActionResult> Upload(CancellationToken ct)
    {
        if (!access.CanWrite())
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
        if (row.Status != "published" && !await access.CanReadUnpublishedAsync(HttpContext, ct)) return NotFound();
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
        if (row.Status != "published" && !await access.CanReadUnpublishedAsync(HttpContext, ct)) return NotFound();

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
        if (!access.CanDelete())
            throw new PermissionDeniedException("Delete not permitted.");
        return await files.DeleteAsync(id, ct) ? NoContent() : NotFound();
    }
}
