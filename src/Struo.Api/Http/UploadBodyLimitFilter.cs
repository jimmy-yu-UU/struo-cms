using Microsoft.AspNetCore.Mvc.Filters;
using Struo.Api.Auth;
using Struo.Application.Files;
using Struo.Domain.Query;

namespace Struo.Api.Http;

/// <summary>
/// Gives the upload endpoint its own request-body cap and reads its multipart form. MVC reads a form
/// request's body while it binds the action, so the caller's write grant has to be checked, the cap put
/// in place and an over-cap body refused in a resource filter, which runs before binding. Authorization
/// has already run by then. A caller without the write grant on files is refused before any of the body
/// is read, with the refusal the action gives.
/// </summary>
internal sealed class UploadBodyLimitFilter(
    FileStorageOptions options, IFileAccessPolicy access, ILogger<UploadBodyLimitFilter> logger)
    : IAsyncResourceFilter
{
    public async Task OnResourceExecutionAsync(ResourceExecutingContext context, ResourceExecutionDelegate next)
    {
        var request = context.HttpContext.Request;
        if (request.HasFormContentType)
        {
            if (!access.CanWrite())
                throw new PermissionDeniedException("Write not permitted.");
            await UploadRequestBody.ReadFormAsync(
                request, options.MaxUploadBytes, logger, context.HttpContext.RequestAborted);
        }

        await next();
    }
}
