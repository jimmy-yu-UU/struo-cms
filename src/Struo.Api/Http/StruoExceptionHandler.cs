using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Struo.Domain.Query;

namespace Struo.Api.Http;

/// <summary>
/// Maps StruoCMS domain exceptions to the unified error envelope (mirrors the GraphQL
/// <c>StruoErrorFilter</c> code mapping). Replaces the inline try/catch middleware previously in
/// Program.cs. Unmapped exceptions are masked (no internal detail leaked) and logged server-side.
/// </summary>
public sealed class StruoExceptionHandler(ILogger<StruoExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken ct)
    {
        if (httpContext.Response.HasStarted) return false;
        var authenticated = httpContext.User.Identity?.IsAuthenticated == true;
        var (status, body) = Map(exception, authenticated, logger);
        httpContext.Response.StatusCode = status;
        await httpContext.Response.WriteAsJsonAsync(Envelope.Error(body.Code, body.Message, body.Details), ct);
        return true;
    }

    /// <summary>Pure mapping: exception → (status, ErrorBody). Unmapped → 500 masked (logged if a logger is supplied).</summary>
    public static (int Status, ErrorBody Body) Map(Exception exception, bool authenticated, ILogger? logger = null) =>
        exception switch
        {
            PermissionDeniedException when !authenticated =>
                (StatusCodes.Status401Unauthorized, new ErrorBody(ErrorCodes.Unauthorized, exception.Message)),
            PermissionDeniedException =>
                (StatusCodes.Status403Forbidden, new ErrorBody(ErrorCodes.Forbidden, exception.Message)),
            CollectionNotFoundException =>
                (StatusCodes.Status404NotFound, new ErrorBody(ErrorCodes.NotFound, exception.Message)),
            RelationConflictException or ConcurrencyConflictException =>
                (StatusCodes.Status409Conflict, new ErrorBody(ErrorCodes.Conflict, exception.Message)),
            QueryException =>
                (StatusCodes.Status400BadRequest, new ErrorBody(ErrorCodes.BadUserInput, exception.Message)),
            _ => LogAndMask(exception, logger),
        };

    private static (int, ErrorBody) LogAndMask(Exception exception, ILogger? logger)
    {
        logger?.LogError(exception, "Unhandled API exception");
        return (StatusCodes.Status500InternalServerError,
            new ErrorBody(ErrorCodes.Internal, "An internal error occurred."));
    }
}
