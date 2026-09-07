// src/Struo.Api/GraphQl/StruoErrorFilter.cs
using HotChocolate;
using HotChocolate.Execution;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Struo.Api.Http;

namespace Struo.Api.GraphQl;

/// <summary>
/// Maps StruoCMS domain exceptions to GraphQL errors carrying a stable <c>code</c> extension.
/// The exception→code decision is delegated to the shared <see cref="DomainErrorMap"/> — the same
/// single source the REST <see cref="StruoExceptionHandler"/> uses — so the two protocols can never
/// drift (notably: an unauthenticated <c>PermissionDeniedException</c> is <c>UNAUTHORIZED</c> on
/// both). GraphQL only stamps the code and message (HTTP 200 semantics are unchanged); it does not
/// carry an HTTP status. Unmapped exceptions are masked and logged server-side.
/// </summary>
public sealed class StruoErrorFilter(
    ILogger<StruoErrorFilter> logger,
    IHttpContextAccessor httpContextAccessor) : IErrorFilter
{
    public IError OnError(IError error)
    {
        var exception = error.Exception;
        if (exception is null)
            return error; // validation/parse errors — leave as-is

        // No HttpContext (non-HTTP execution, e.g. the in-process request executor) or an absent
        // ClaimsPrincipal ⇒ unauthenticated, mirroring the REST handler's authenticated bit.
        var authenticated = httpContextAccessor.HttpContext?.User.Identity?.IsAuthenticated == true;
        var (code, message) = DomainErrorMap.Map(exception, authenticated);

        if (code == Http.ErrorCodes.Internal)
        {
            logger.LogError(exception, "Unhandled GraphQL resolver exception");
            return error
                .WithMessage(message)
                .WithCode(code)
                .WithException(null!); // IError has no RemoveException() in the installed HotChocolate version
        }

        if (code == Http.ErrorCodes.SearchUnavailable)
        {
            logger.LogWarning(exception, "Search provider unavailable");
            return error.WithMessage(message).WithCode(code).WithException(null!);
        }

        return error.WithMessage(message).WithCode(code);
    }
}
