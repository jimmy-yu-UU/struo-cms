using Microsoft.AspNetCore.Mvc;
using Struo.Infrastructure.Files;

namespace Struo.Api.Controllers;

[ApiController]
[Route("api/files")]
public sealed class FilesController(FileService files) : ControllerBase
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
}
