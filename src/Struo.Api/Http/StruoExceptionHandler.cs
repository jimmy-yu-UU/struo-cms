using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

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

    /// <summary>
    /// Pure mapping: exception → (status, ErrorBody). The exception→code decision is delegated to
    /// <see cref="DomainErrorMap"/> (the single source shared with GraphQL); this method layers the
    /// HTTP status on top and masks/logs the unmapped (internal) case — masking/logging stays here,
    /// not in <see cref="DomainErrorMap"/>.
    /// </summary>
    public static (int Status, ErrorBody Body) Map(Exception exception, bool authenticated, ILogger? logger = null)
    {
        var (code, message) = DomainErrorMap.Map(exception, authenticated);
        if (code == ErrorCodes.Internal)
            logger?.LogError(exception, "Unhandled API exception");
        return (DomainErrorMap.StatusFor(code), new ErrorBody(code, message));
    }
}
