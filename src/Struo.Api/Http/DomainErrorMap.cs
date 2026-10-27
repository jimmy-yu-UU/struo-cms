using Microsoft.AspNetCore.Http;
using Struo.Application.Security;
using Struo.Domain;
using Struo.Domain.Query;
using Struo.Infrastructure.Persistence;

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
    /// yields <see cref="ErrorCodes.Forbidden"/>. A <see cref="StruoDomainException"/> (a fork's own error) is matched first and surfaces
    /// its own code and message. Mapped domain exceptions surface their
    /// client-safe <see cref="System.Exception.Message"/> verbatim. Two cases carry a fixed message
    /// instead, because the exception's own text names internals:
    /// <see cref="SearchUnavailableException"/> (<see cref="SearchUnavailableMessage"/>, the provider
    /// host) and a database unique-key violation (<see cref="UniqueViolationMessage"/>, the
    /// constraint, index or column). Any other unmapped type collapses to
    /// <see cref="ErrorCodes.Internal"/> with a masked generic message (no internal detail leaked);
    /// logging of that case is the caller's responsibility.
    /// </summary>
    public static (string Code, string Message) Map(Exception exception, bool authenticated) =>
        exception switch
        {
            StruoDomainException domain => (domain.Code, domain.Message),
            PermissionDeniedException when !authenticated => (ErrorCodes.Unauthorized, exception.Message),
            PermissionDeniedException => (ErrorCodes.Forbidden, exception.Message),
            CollectionNotFoundException => (ErrorCodes.NotFound, exception.Message),
            FileBlobNotFoundException => (ErrorCodes.NotFound, exception.Message),
            ConcurrencyConflictException => (ErrorCodes.VersionConflict, exception.Message),
            ConflictException => (ErrorCodes.Conflict, exception.Message),
            RelationConflictException => (ErrorCodes.Conflict, exception.Message),
            QueryException => (ErrorCodes.BadUserInput, exception.Message),
            PayloadTooLargeException => (ErrorCodes.PayloadTooLarge, exception.Message),
            SessionRevocationFailedException => (ErrorCodes.SessionRevocationFailed, exception.Message),
            SearchUnavailableException => (ErrorCodes.SearchUnavailable, SearchUnavailableMessage),
            _ when IsUniqueViolation(exception) => (ErrorCodes.Conflict, UniqueViolationMessage),
            _ => (ErrorCodes.Internal, "An internal error occurred."),
        };

    /// <summary>
    /// Fixed client-facing message for <see cref="ErrorCodes.SearchUnavailable"/>. Never the
    /// exception's own <see cref="System.Exception.Message"/>, which may name an internal provider
    /// host — that detail only ever reaches the Warning-level server log.
    /// </summary>
    public const string SearchUnavailableMessage = "Search is temporarily unavailable.";

    /// <summary>True when <paramref name="exception"/> is a database duplicate-key violation.</summary>
    public static bool IsUniqueViolation(Exception exception) => UniqueViolation.IsMatch(exception);

    /// <summary>
    /// Fixed client-facing message for a database unique-key violation. Never the provider's own
    /// message, which names the constraint, index or column.
    /// </summary>
    public const string UniqueViolationMessage = "A record with the same unique value already exists.";

    /// <summary>
    /// REST-only: the HTTP status for <paramref name="exception"/>. A <see cref="StruoDomainException"/>
    /// carries its own status; any other exception takes the status of its mapped <paramref name="code"/>.
    /// </summary>
    public static int StatusFor(Exception exception, string code) =>
        exception is StruoDomainException domain ? domain.Status : StatusFor(code);

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
        ErrorCodes.SearchUnavailable => StatusCodes.Status503ServiceUnavailable,
        _ => StatusCodes.Status500InternalServerError,
    };
}
