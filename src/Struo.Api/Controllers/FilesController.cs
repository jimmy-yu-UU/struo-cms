using Microsoft.AspNetCore.Mvc;
using Struo.Application.Files;
// Alias to avoid importing the Struo.Infrastructure.Files namespace, whose `File` type would
// clash with ControllerBase.File(...) used by the download action.
using FileService = Struo.Infrastructure.Files.FileService;

namespace Struo.Api.Controllers;

[ApiController]
[Route("api/files")]
public sealed class FilesController(FileService files, IFileStorage storage, FileStorageOptions options) : ControllerBase
{
    [HttpPost]
    public async Task<IActionResult> Upload(CancellationToken ct)
    {
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
        if (row is null || row.Status != "published") return NotFound();
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
        if (row is null || row.Status != "published") return NotFound();

        var presigned = await storage.GetPresignedUrlAsync(
            row.StorageKey, TimeSpan.FromSeconds(options.S3.PresignTtlSeconds), ct);
        if (presigned is not null) return Redirect(presigned);   // 302 (S3/MinIO)

        var stream = await storage.OpenReadAsync(row.StorageKey, ct);
        return File(stream, row.ContentType, fileDownloadName: row.FileName);
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct) =>
        await files.DeleteAsync(id, ct) ? NoContent() : NotFound();
}
