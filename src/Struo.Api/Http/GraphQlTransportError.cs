using Microsoft.AspNetCore.Http;

namespace Struo.Api.Http;

/// <summary>
/// How a request refused in the transport layer of <c>/graphql</c> is answered: <c>{ errors: [...] }</c>,
/// the shape GraphQL clients read, rather than the REST error envelope.
/// </summary>
internal static class GraphQlTransportError
{
    /// <summary>The path the GraphQL endpoint is mapped at; a path under it is the endpoint too.</summary>
    internal const string EndpointPath = "/graphql";

    internal static bool IsGraphQl(HttpRequest request) =>
        request.Path.StartsWithSegments(EndpointPath, StringComparison.OrdinalIgnoreCase);

    internal static Task WriteAsync(HttpContext context, int status, string code, string message)
    {
        context.Response.StatusCode = status;
        var error = new Dictionary<string, object?>
        {
            ["message"] = message,
            ["extensions"] = new Dictionary<string, object?>
            {
                ["code"] = code,
                ["traceId"] = RequestTraceId.For(context),
            },
        };
        return context.Response.WriteAsJsonAsync(
            new Dictionary<string, object?> { ["errors"] = new[] { error } }, context.RequestAborted);
    }
}
