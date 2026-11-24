using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;

namespace Struo.Tests.Api;

/// <summary>
/// Gives every request with a body to a path under <c>pathPrefix</c> a body whose read fails with the given
/// exception. With <c>clientGone</c>, the request's <c>RequestAborted</c> token is cancelled as the read fails.
/// </summary>
internal sealed class FailingBodyPathFilter(string pathPrefix, Exception failure, bool clientGone = false)
    : IStartupFilter
{
    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
    {
        app.Use(async (context, nextMiddleware) =>
        {
            var hasBody = !HttpMethods.IsGet(context.Request.Method) && !HttpMethods.IsHead(context.Request.Method);
            if (!hasBody || !context.Request.Path.StartsWithSegments(pathPrefix, StringComparison.OrdinalIgnoreCase))
            {
                await nextMiddleware();
                return;
            }

            using var gone = new CancellationTokenSource();
            if (clientGone) context.RequestAborted = gone.Token;
            context.Request.Body = new FailingBodyStream(failure, clientGone ? gone.Cancel : null);
            await nextMiddleware();
        });
        next(app);
    };
}
