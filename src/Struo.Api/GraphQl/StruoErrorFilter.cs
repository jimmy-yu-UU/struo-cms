// src/Struo.Api/GraphQl/StruoErrorFilter.cs
using HotChocolate;
using HotChocolate.Execution;
using Microsoft.Extensions.Logging;
using Struo.Domain.Query;

namespace Struo.Api.GraphQl;

/// <summary>
/// Maps StruoCMS domain exceptions to GraphQL errors carrying a stable <c>code</c> extension,
/// mirroring the REST exception→HTTP middleware (see Program.cs). Unmapped exceptions are masked
/// (no internal detail leaked) and logged server-side.
/// </summary>
public sealed class StruoErrorFilter(ILogger<StruoErrorFilter> logger) : IErrorFilter
{
    public IError OnError(IError error)
    {
        var exception = error.Exception;
        switch (exception)
        {
            case null:
                return error; // validation/parse errors — leave as-is
            case QueryException:
                // Domain exceptions carry client-safe messages (mirrors the REST middleware in
                // Program.cs, which also surfaces ex.Message verbatim for these types).
                return error.WithMessage(exception.Message).WithCode("BAD_USER_INPUT");
            case CollectionNotFoundException:
                return error.WithMessage(exception.Message).WithCode("NOT_FOUND");
            case PermissionDeniedException:
                return error.WithMessage(exception.Message).WithCode("FORBIDDEN");
            case RelationConflictException:
            case ConcurrencyConflictException:
                return error.WithMessage(exception.Message).WithCode("CONFLICT");
            default:
                logger.LogError(exception, "Unhandled GraphQL resolver exception");
                return error
                    .WithMessage("An internal error occurred.")
                    .WithCode("INTERNAL_SERVER_ERROR")
                    .WithException(null!); // IError has no RemoveException() in the installed HotChocolate version
        }
    }
}
