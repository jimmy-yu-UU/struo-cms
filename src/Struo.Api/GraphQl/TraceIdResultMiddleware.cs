using HotChocolate;
using HotChocolate.Execution;
using Microsoft.AspNetCore.Http;
using Struo.Api.Http;

namespace Struo.Api.GraphQl;

/// <summary>
/// Stamps <c>extensions.traceId</c> on every error of a GraphQL result that lacks one. Errors raised by
/// document validation and request rules never reach <see cref="StruoErrorFilter"/>, so this covers them
/// with the same id the REST error envelope carries.
/// </summary>
internal static class TraceIdResultMiddleware
{
    internal static HotChocolate.Execution.RequestDelegate Create(HotChocolate.Execution.RequestDelegate next) => async context =>
    {
        await next(context);
        if (context.Result is not OperationResult { Errors.Count: > 0 } result) return;

        var httpContext = context.RequestServices.GetService(typeof(IHttpContextAccessor)) is IHttpContextAccessor accessor
            ? accessor.HttpContext
            : null;
        if (RequestTraceId.For(httpContext) is not { } traceId) return;

        result.Errors = [.. result.Errors.Select(e => WithTraceId(e, traceId))];
    };

    internal static IError WithTraceId(IError error, string traceId) =>
        error.Extensions?.ContainsKey("traceId") == true ? error : error.SetExtension("traceId", traceId);
}
