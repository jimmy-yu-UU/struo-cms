using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;

namespace Struo.Tests.Api;

/// <summary>Gives every request with a body to a path under <c>pathPrefix</c> a body whose read fails with the given exception.</summary>
internal sealed class FailingBodyPathFilter(string pathPrefix, Exception failure) : IStartupFilter
{
    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
    {
        app.Use(async (context, nextMiddleware) =>
        {
            var hasBody = !HttpMethods.IsGet(context.Request.Method) && !HttpMethods.IsHead(context.Request.Method);
            if (hasBody && context.Request.Path.StartsWithSegments(pathPrefix, StringComparison.OrdinalIgnoreCase))
                context.Request.Body = new FailingBodyStream(failure);
            await nextMiddleware();
        });
        next(app);
    };
}
