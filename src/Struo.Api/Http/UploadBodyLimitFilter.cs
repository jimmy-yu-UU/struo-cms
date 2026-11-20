using Microsoft.AspNetCore.Mvc.Filters;
using Struo.Application.Files;

namespace Struo.Api.Http;

/// <summary>
/// Gives the upload endpoint its own request-body cap and reads its multipart form. MVC reads a form
/// request's body while it binds the action, so the cap has to be in place, and an over-cap body
/// refused, in a resource filter, which runs before binding. Authorization has already run by then.
/// </summary>
public sealed class UploadBodyLimitFilter(FileStorageOptions options) : IAsyncResourceFilter
{
    public async Task OnResourceExecutionAsync(ResourceExecutingContext context, ResourceExecutionDelegate next)
    {
        var request = context.HttpContext.Request;
        if (request.HasFormContentType)
            await UploadRequestBody.ReadFormAsync(request, options.MaxUploadBytes, context.HttpContext.RequestAborted);
        await next();
    }
}
