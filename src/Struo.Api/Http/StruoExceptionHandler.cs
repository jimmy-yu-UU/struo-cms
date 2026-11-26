using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Struo.Domain;

namespace Struo.Api.Http;

/// <summary>
/// Maps StruoCMS domain exceptions to the unified error envelope (mirrors the GraphQL
/// <c>StruoErrorFilter</c> code mapping); a request to <c>/graphql</c> gets the same status, code and
/// message as a GraphQL error. Unmapped exceptions are masked (no internal detail leaked) and logged server-side.
/// </summary>
public sealed class StruoExceptionHandler(ILogger<StruoExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        if (httpContext.Response.HasStarted) return false;
        var authenticated = httpContext.User.Identity?.IsAuthenticated == true;
        var (status, body) = Map(exception, authenticated, logger);
        if (GraphQlTransportError.IsGraphQl(httpContext.Request))
        {
            await GraphQlTransportError.WriteAsync(httpContext, status, body.Code, body.Message);
            return true;
        }

        httpContext.Response.StatusCode = status;
        await httpContext.Response.WriteAsJsonAsync(Envelope.Error(body.Code, body.Message, body.Details, RequestTraceId.For(httpContext)), cancellationToken);
        return true;
    }

    /// <summary>
    /// Pure mapping: exception → (status, ErrorBody). The exception→code decision is delegated to
    /// <see cref="DomainErrorMap"/> (the single source shared with GraphQL); this method layers the
    /// HTTP status on top and masks/logs the unmapped (internal) case — masking/logging stays here,
    /// not in <see cref="DomainErrorMap"/>.
    /// </summary>
    public static (int Status, ErrorBody Body) Map(Exception exception, bool authenticated, ILogger? logger = null)
    {
        var (code, message) = DomainErrorMap.Map(exception, authenticated);
        // A fork's own error is a client error by construction; its code may equal one the branches below
        // key on in Log, so the type decides before the code does.
        if (exception is StruoDomainException)
            return (DomainErrorMap.StatusFor(exception, code), new ErrorBody(code, message));
        Log(exception, code, logger);
        return (DomainErrorMap.StatusFor(exception, code),
            new ErrorBody(code, message, DomainErrorMap.DetailsFor(exception)));
    }

    private static void Log(Exception exception, string code, ILogger? logger)
    {
        if (code == ErrorCodes.Internal)
            logger?.LogError(exception, "Unhandled API exception");
        else if (code == ErrorCodes.SearchUnavailable)
            logger?.LogWarning(exception, "Search provider unavailable");
        else if (DomainErrorMap.IsUniqueViolation(exception))
            logger?.LogWarning(exception, "Unique constraint violation");
        else if (DomainErrorMap.IsDeadlockVictim(exception))
            logger?.LogWarning(exception, "The request lost a database deadlock");
        else if (exception is BadHttpRequestException rejected)
            RequestBodyReadLog.Rejected(logger, rejected);
    }
}
