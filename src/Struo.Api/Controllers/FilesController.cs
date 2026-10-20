using System.Globalization;
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
    ImageTransformServices images, ILogger<FilesController> logger)
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
        // A non-published file needs a signed-in caller holding the file write grant, not merely a
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
        // Same gate as Get above: a non-published file needs a signed-in caller holding the file write
        // grant, not merely a logged-in session. 404 (not 403) so existence isn't leaked.
        if (row.Status != "published" && !access.CanReadUnpublished()) return NotFound();

        // On-the-fly image transform. Only when the caller actually asked for one (at least one
        // of width/height/format present), the content behind this row is a transformable image type, and
        // the feature is enabled. Otherwise fall straight through to the existing passthrough behavior below —
        // unchanged for every non-image file and for image requests with no transform params.
        var imageTransform = options.ImageTransform;
        var wantsTransform = imageTransform.Enabled
            && (width is not null || height is not null || format is not null)
            && imageTransform.TransformableContentTypes.Contains(row.ContentType, StringComparer.OrdinalIgnoreCase);

        if (wantsTransform)
        {
            if (!ImageTransformRequestNormalizer.TryNormalize(
                    new RawImageTransformQuery(width, height, format, fit, quality), imageTransform, out var req, out var requestError))
                return ApiResults.Fail(StatusCodes.Status400BadRequest, ErrorCodes.BadUserInput, requestError);

            // Version stamp: row.Version (AuditableEntity's optimistic-concurrency counter) rather than
            // UpdatedAt, because UpdatedAt is a non-nullable DateTime here (no "unset" sentinel to
            // reason about) and Version is already the codebase's existing monotonic per-row change
            // counter — it increments on every update, including a future re-upload/replace, so stale
            // variants naturally miss instead of serving bytes from a since-replaced file.
            var fileVersion = row.Version.ToString();
            var key = images.Cache.DeriveKey(id, fileVersion, req);

            var cached = await images.Cache.TryGetAsync(key, ct);
            if (cached is not null)
                return File(cached, ImageContentTypes.ContentTypeFor(req.Format));

            var gated = await TransformUnderGateAsync(row, key, req, imageTransform, ct);
            if (gated.Response is not null) return gated.Response;
            var transformed = gated.Transformed!.Value;

            // The gate slot is already released: the cache write's size sweep must not hold it.
            try
            {
                await images.Cache.SetAsync(key, transformed.Bytes, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // An unwritable cache (disk full, read-only directory) costs only the caching.
                logger.LogWarning(ex, "Image variant {VariantKey} for file {FileId} could not be cached; serving it uncached.", key, id);
            }
            return File(transformed.Bytes, transformed.ContentType);
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

    // Outcome of the gated transform: either a response to return as is, or the transformed image.
    private readonly record struct GatedTransform(IActionResult? Response, ImageTransformResult? Transformed);

    // Holds one transform slot while the source is read and transformed; the slot is released when
    // this method returns, before the caller writes the cache.
    private async Task<GatedTransform> TransformUnderGateAsync(
        Struo.Infrastructure.Files.File row, string key, ImageTransformRequest req,
        FileStorageOptions.ImageTransformOptions imageTransform, CancellationToken ct)
    {
        // The gate is taken before the source is read so waiting requests hold no source bytes.
        using var lease = await images.Gate.TryEnterAsync(ct);
        if (lease is null)
        {
            Response.Headers.RetryAfter = Math.Max(1, imageTransform.QueueTimeoutSeconds)
                .ToString(CultureInfo.InvariantCulture);
            return new(ApiResults.Fail(StatusCodes.Status503ServiceUnavailable, ErrorCodes.ImageTransformBusy,
                "Image transforms are busy; retry shortly."), null);
        }

        // A request that waited here may find the variant stored by the one it queued behind.
        var cached = await images.Cache.TryGetAsync(key, ct);
        if (cached is not null)
            return new(File(cached, ImageContentTypes.ContentTypeFor(req.Format)), null);

        byte[] sourceBytes;
        await using (var src = await storage.OpenReadAsync(row.StorageKey, ct))
        using (var ms = new MemoryStream())
        {
            await src.CopyToAsync(ms, ct);
            sourceBytes = ms.ToArray();
        }

        // Header-only probe: a source with unreadable or oversized dimensions is served as is
        // instead of being decoded.
        using (var probe = new MemoryStream(sourceBytes, writable: false))
        {
            var dims = images.Dimensions.TryRead(probe, row.ContentType);
            if (dims is not { Width: > 0, Height: > 0 }
                || (long)dims.Value.Width * dims.Value.Height > imageTransform.MaxSourcePixels)
                return new(File(sourceBytes, row.ContentType, fileDownloadName: row.FileName), null);
        }

        ImageTransformResult? result;
        try
        {
            result = images.Transformer.Transform(sourceBytes, req, imageTransform.MaxSourcePixels);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Never silently swallow: log with enough context to investigate (corrupt upload,
            // unsupported source encoding, libvips failure, ...), then fall back to serving the
            // original bytes so the request still succeeds for the caller.
            logger.LogWarning(ex,
                "Image transform failed for file {FileId}; falling back to original bytes.", row.Id);
            return new(File(sourceBytes, row.ContentType, fileDownloadName: row.FileName), null);
        }

        // The decoder's own header size is over the limit (e.g. a derived AVIF the probe misjudged).
        return result is null
            ? new(File(sourceBytes, row.ContentType, fileDownloadName: row.FileName), null)
            : new(null, result);
    }

    // Default DELETE is trash (recoverable); ?purge=true is the permanent hard delete
    // (FileService.DeleteAsync — row + sidecar translations + blob + any `SiteSettings.LogoFileId`
    // reference + the relation rules: Restrict guard, SetNull of referencing keys, junction rows). Trash
    // applies the Restrict guard and clears the logo reference.
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
