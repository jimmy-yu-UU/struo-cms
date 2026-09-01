using Microsoft.AspNetCore.Http;
using Struo.Application.Security;
using Struo.Domain.Query;

namespace Struo.Api.Http;

/// <summary>
/// Single source of truth for StruoCMS domain-exception → stable error <c>code</c> mapping,
/// shared by the REST <see cref="StruoExceptionHandler"/> and the GraphQL
/// <c>StruoErrorFilter</c> so the two protocols can never drift. Pure and side-effect free:
/// it never logs (the REST handler owns logging/masking of unmapped exceptions) and it never
/// touches HTTP — callers layer transport concerns on top (REST derives an HTTP status via
/// <see cref="StatusFor"/>; GraphQL only stamps the code and keeps HTTP 200 semantics).
/// </summary>
public static class DomainErrorMap
{
    /// <summary>
    /// Maps <paramref name="exception"/> to an <c>(code, message)</c> pair using
    /// <see cref="ErrorCodes"/> constants. An unauthenticated <see cref="PermissionDeniedException"/>
    /// yields <see cref="ErrorCodes.Unauthorized"/> (REST semantics govern); an authenticated one
    /// yields <see cref="ErrorCodes.Forbidden"/>. Mapped domain exceptions surface their
    /// client-safe <see cref="System.Exception.Message"/> verbatim. Any unmapped type collapses to
    /// <see cref="ErrorCodes.Internal"/> with a masked generic message (no internal detail leaked);
    /// logging of that case is the caller's responsibility.
    /// </summary>
    public static (string Code, string Message) Map(Exception exception, bool authenticated) =>
        exception switch
        {
            PermissionDeniedException when !authenticated => (ErrorCodes.Unauthorized, exception.Message),
            PermissionDeniedException => (ErrorCodes.Forbidden, exception.Message),
            CollectionNotFoundException => (ErrorCodes.NotFound, exception.Message),
            FileBlobNotFoundException => (ErrorCodes.NotFound, exception.Message),
            ConcurrencyConflictException => (ErrorCodes.VersionConflict, exception.Message),
            RelationConflictException => (ErrorCodes.Conflict, exception.Message),
            QueryException => (ErrorCodes.BadUserInput, exception.Message),
            PayloadTooLargeException => (ErrorCodes.PayloadTooLarge, exception.Message),
            SessionRevocationFailedException => (ErrorCodes.SessionRevocationFailed, exception.Message),
            _ => (ErrorCodes.Internal, "An internal error occurred."),
        };

    /// <summary>
    /// REST-only: derives the HTTP status for a mapped <paramref name="code"/>. Co-located with the
    /// exception→code map so all code/status knowledge lives in one place (the inverse direction,
    /// status→code, stays in <see cref="ErrorCodes.ForStatus"/> and is unaffected).
    /// </summary>
    public static int StatusFor(string code) => code switch
    {
        ErrorCodes.Unauthorized => StatusCodes.Status401Unauthorized,
        ErrorCodes.Forbidden => StatusCodes.Status403Forbidden,
        ErrorCodes.NotFound => StatusCodes.Status404NotFound,
        ErrorCodes.Conflict => StatusCodes.Status409Conflict,
        ErrorCodes.VersionConflict => StatusCodes.Status409Conflict,
        ErrorCodes.BadUserInput => StatusCodes.Status400BadRequest,
        ErrorCodes.Validation => StatusCodes.Status400BadRequest,
        ErrorCodes.PayloadTooLarge => StatusCodes.Status413PayloadTooLarge,
        _ => StatusCodes.Status500InternalServerError,
    };
}
