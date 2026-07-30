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
    FileService files, IFileStorage storage, FileStorageOptions options, IFileAccessPolicy access,
    IImageTransformer transformer, IImageVariantCache variantCache, ILogger<FilesController> logger)
    : ControllerBase
{
    // The media library is the "file" collection. Mutations go through the dedicated file storage
    // pipeline rather than the generic ItemService, so RBAC must be enforced here too — otherwise any
    // authenticated caller (incl. a role-less SSO user) could upload or delete any file, bypassing the
    // per-collection grants that govern every other collection. That RBAC policy lives in
    // IFileAccessPolicy so it isn't duplicated with PermissionResolutionMiddleware.
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

        Guid? folderId = null;
        var folderRaw = form["folderId"].ToString();
        if (!string.IsNullOrEmpty(folderRaw))
        {
            if (!Guid.TryParse(folderRaw, out var parsedFolder))
                return ApiResults.Fail(StatusCodes.Status400BadRequest, ErrorCodes.BadUserInput, "Invalid 'folderId'.");
            folderId = parsedFolder;
        }

        await using var stream = file.OpenReadStream();
        var created = await files.UploadAsync(stream, file.FileName, file.ContentType, file.Length, folderId, ct);
        return Created($"/api/files/{created.Id}", new
        {
            id = created.Id, fileName = created.FileName, contentType = created.ContentType,
            size = created.Size, width = created.Width, height = created.Height, status = created.Status,
            folderId = created.FolderId
        });
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct)
    {
        var row = await files.GetAsync(id, ct);
        if (row is null) return NotFound();
        // A non-published file requires a genuine per-collection read grant, not merely a
        // logged-in session (a role-less JIT/SSO user could otherwise fetch any draft). 404 (not 403)
        // so existence isn't leaked.
        if (row.Status != "published" && !access.CanReadUnpublished()) return NotFound();
        return Ok(new
        {
            id = row.Id, fileName = row.FileName, contentType = row.ContentType,
            size = row.Size, width = row.Width, height = row.Height, status = row.Status,
            folderId = row.FolderId
        });
    }

    [HttpGet("{id:guid}/content")]
    public async Task<IActionResult> Download(
        Guid id,
        [FromQuery] int? width, [FromQuery] int? height,
        [FromQuery] string? format, [FromQuery] string? fit, [FromQuery] int? quality,
        CancellationToken ct = default)
    {
        var row = await files.GetAsync(id, ct);
        if (row is null) return NotFound();
        // Same gate as Get above: a non-published file requires a genuine per-collection read grant,
        // not merely a logged-in session. 404 (not 403) so existence isn't leaked.
        if (row.Status != "published" && !access.CanReadUnpublished()) return NotFound();

        // On-the-fly image transform. Only when the caller actually asked for one (at least one
        // of width/height/format present), the content behind this row is an image, and the feature is
        // enabled. Otherwise fall straight through to the existing passthrough behavior below —
        // unchanged for every non-image file and for image requests with no transform params.
        var imageTransform = options.ImageTransform;
        var wantsTransform = imageTransform.Enabled
            && (width is not null || height is not null || format is not null)
            && row.ContentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase);

        if (wantsTransform)
        {
            var normalizedFormat = (format ?? "").ToLowerInvariant();
            if (format is not null && !imageTransform.AllowedFormats.Contains(normalizedFormat))
                return ApiResults.Fail(StatusCodes.Status400BadRequest, ErrorCodes.BadUserInput,
                    $"Unsupported format '{format}'.");

            var req = new ImageTransformRequest(
                Width: width is null ? null : Math.Clamp(width.Value, 1, imageTransform.MaxWidth),
                Height: height is null ? null : Math.Clamp(height.Value, 1, imageTransform.MaxHeight),
                Format: format is null ? null : normalizedFormat,
                Fit: string.IsNullOrEmpty(fit) ? "inside" : fit,
                Quality: Math.Clamp(quality ?? imageTransform.DefaultQuality, 1, 100));

            // Version stamp: row.Version (AuditableEntity's optimistic-concurrency counter) rather than
            // UpdatedAt, because UpdatedAt is a non-nullable DateTime here (no "unset" sentinel to
            // reason about) and Version is already the codebase's existing monotonic per-row change
            // counter — it increments on every update, including a future re-upload/replace, so stale
            // variants naturally miss instead of serving bytes from a since-replaced file.
            var fileVersion = row.Version.ToString();
            var key = variantCache.DeriveKey(id, fileVersion, req);

            var cached = await variantCache.TryGetAsync(key, ct);
            if (cached is not null)
                return File(cached, ImageContentTypes.ContentTypeFor(req.Format));

            byte[] sourceBytes;
            await using (var src = await storage.OpenReadAsync(row.StorageKey, ct))
            using (var ms = new MemoryStream())
            {
                await src.CopyToAsync(ms, ct);
                sourceBytes = ms.ToArray();
            }

            ImageTransformResult result;
            try
            {
                result = transformer.Transform(sourceBytes, req);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Never silently swallow: log with enough context to investigate (corrupt upload,
                // unsupported source encoding, libvips failure, ...), then fall back to serving the
                // original bytes so the request still succeeds for the caller — a broken thumbnail is
                // a worse UX than an un-transformed original, but a swallowed error is worse still.
                logger.LogWarning(ex,
                    "Image transform failed for file {FileId}; falling back to original bytes.", id);
                var fallback = await storage.OpenReadAsync(row.StorageKey, ct);
                return File(fallback, row.ContentType, fileDownloadName: row.FileName);
            }

            await variantCache.SetAsync(key, result.Bytes, ct);
            return File(result.Bytes, result.ContentType);
        }

        // The admin SPA loads thumbnails/previews from this endpoint, so by default the API streams
        // the bytes itself. Redirecting to storage is an explicit deployment opt-in
        // (Struo:Files:PresignedRedirect) for setups where the browser can reach storage/CDN.
        if (options.PresignedRedirect)
        {
            var presigned = await storage.GetPresignedUrlAsync(
                row.StorageKey, TimeSpan.FromSeconds(options.S3.PresignTtlSeconds), ct);
            if (presigned is not null) return Redirect(presigned);   // 302 (S3/MinIO)
        }

        var stream = await storage.OpenReadAsync(row.StorageKey, ct);
        return File(stream, row.ContentType, fileDownloadName: row.FileName);
    }

    // Default DELETE is trash (recoverable); ?purge=true is the permanent hard delete
    // (FileService.DeleteAsync — row + sidecar translations + blob + any site_settings.logofileid
    // reference, same as before this change).
    [HttpDelete("{id:guid}")]
    [Authorize(AuthenticationSchemes = AuthSchemes.CookieOrBearer)]
    public async Task<IActionResult> Delete(Guid id, [FromQuery] bool purge = false, CancellationToken ct = default)
    {
        if (!access.CanDelete())
            throw new PermissionDeniedException("Delete not permitted.");
        var ok = purge ? await files.DeleteAsync(id, ct) : await files.TrashAsync(id, ct);
        return ok ? NoContent() : NotFound();
    }

    [HttpPost("{id:guid}/restore")]
    [Authorize(AuthenticationSchemes = AuthSchemes.CookieOrBearer)]
    public async Task<IActionResult> Restore(Guid id, CancellationToken ct)
    {
        if (!access.CanDelete())
            throw new PermissionDeniedException("Delete not permitted.");
        return await files.RestoreAsync(id, ct) ? NoContent() : NotFound();
    }
}
