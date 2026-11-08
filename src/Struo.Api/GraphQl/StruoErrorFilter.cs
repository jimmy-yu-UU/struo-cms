// src/Struo.Api/GraphQl/StruoErrorFilter.cs
using HotChocolate;
using HotChocolate.Execution;
using HotChocolate.Language;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Struo.Api.Http;
using Struo.Domain;

namespace Struo.Api.GraphQl;

/// <summary>
/// Maps StruoCMS domain exceptions to GraphQL errors carrying a stable <c>code</c> extension.
/// The exception→code decision is delegated to the shared <see cref="DomainErrorMap"/> — the same
/// single source the REST <see cref="StruoExceptionHandler"/> uses — so the two protocols can never
/// drift (notably: an unauthenticated <c>PermissionDeniedException</c> is <c>UNAUTHORIZED</c> on
/// both). GraphQL stamps the code and message, plus <c>extensions.details</c> when the exception
/// carries them (HTTP 200 semantics are unchanged); it does not carry an HTTP status. Unmapped
/// exceptions are masked and logged server-side.
/// </summary>
public sealed class StruoErrorFilter(
    ILogger<StruoErrorFilter> logger,
    IHttpContextAccessor httpContextAccessor) : IErrorFilter
{
    public IError OnError(IError error)
    {
        var mapped = MapError(error);
        return RequestTraceId.For(httpContextAccessor.HttpContext) is { } traceId
            ? mapped.SetExtension("traceId", traceId)
            : mapped;
    }

    private IError MapError(IError error)
    {
        var exception = error.Exception;
        if (exception is null)
            return error; // validation/parse errors — no exception to map

        // No HttpContext (non-HTTP execution, e.g. the in-process request executor) or an absent
        // ClaimsPrincipal ⇒ unauthenticated, mirroring the REST handler's authenticated bit.
        var authenticated = httpContextAccessor.HttpContext?.User.Identity?.IsAuthenticated == true;
        var (code, message) = DomainErrorMap.Map(exception, authenticated);

        // A fork's own error is a client error by construction; its code may equal one the branches below
        // key on, so the type decides before the code does.
        if (exception is StruoDomainException)
            return error.WithMessage(message).WithCode(code);

        if (LongOverflowCode(error, exception) is { } overflowCode)
            return error.WithMessage(LongOverflowMessage(overflowCode)).WithCode(overflowCode).WithException(null!);

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

        if (code == Http.ErrorCodes.Conflict && DomainErrorMap.IsUniqueViolation(exception))
        {
            logger.LogWarning(exception, "Unique constraint violation");
            return error.WithMessage(message).WithCode(code).WithException(null!);
        }

        var coded = error.WithMessage(message).WithCode(code);
        var details = DomainErrorMap.DetailsFor(exception);
        return details is null
            ? coded
            : coded.SetExtension("details", details.Select(ToExtension).ToList());
    }

    // HotChocolate's Long scalar throws, instead of reporting a coercion error, for an integer outside the
    // 64-bit range: InvalidFormatException from a literal, FormatException from a variable. Only an error
    // HotChocolate itself raised while coercing a Long input (it stamps `inputPath` and `fieldType`) counts,
    // so a FormatException thrown inside a resolver stays an unhandled error. A literal also carries the
    // `coordinate` of the field or argument; one named `version` is INVALID_VERSION, as on REST. A variable
    // is named by the client, so its target is unknown and it is BAD_USER_INPUT.
    private static string? LongOverflowCode(IError error, Exception exception)
    {
        if (exception is not (InvalidFormatException or FormatException)) return null;
        var extensions = error.Extensions;
        if (extensions is null || !extensions.TryGetValue("fieldType", out var fieldType) || fieldType?.ToString() != "Long"
            || !extensions.TryGetValue("inputPath", out var inputPath) || inputPath is null)
            return null;
        var isLiteral = extensions.ContainsKey("coordinate");
        var target = inputPath.ToString()?.Split('.')[^1];
        return isLiteral && target == "version" ? Http.ErrorCodes.InvalidVersion : Http.ErrorCodes.BadUserInput;
    }

    private static string LongOverflowMessage(string code) =>
        code == Http.ErrorCodes.InvalidVersion
            ? "version must be an integer within the range of a 64-bit integer."
            : "The value is outside the range of a 64-bit integer.";

    private static Dictionary<string, object?> ToExtension(ValidationDetail d)
    {
        var item = new Dictionary<string, object?> { ["field"] = d.Field, ["message"] = d.Message };
        if (d.Locale is not null) item["locale"] = d.Locale;
        if (d.MaxBytes is not null) item["maxBytes"] = d.MaxBytes;
        return item;
    }
}
