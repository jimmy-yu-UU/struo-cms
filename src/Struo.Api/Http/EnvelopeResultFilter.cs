using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace Struo.Api.Http;

/// <summary>
/// Wraps successful MVC results in <see cref="SuccessEnvelope"/> and converts bare error status
/// results into <see cref="ErrorEnvelope"/>. Results already carrying an envelope (produced by the
/// Fail helper / validation factory) are left untouched, so wrapping is idempotent. 204, binary file
/// streams, redirects, challenges and empty results pass through unchanged. The exception handler
/// writes its response directly and never reaches this filter.
/// </summary>
public sealed class EnvelopeResultFilter : IAlwaysRunResultFilter
{
    public void OnResultExecuting(ResultExecutingContext context)
    {
        if (BuildEnvelope(context.Result) is { } replacement) context.Result = replacement;
    }

    public void OnResultExecuted(ResultExecutedContext context) { }

    /// <summary>Returns the replacement result, or <c>null</c> to leave the result untouched.</summary>
    public static IActionResult? BuildEnvelope(IActionResult result)
    {
        switch (result)
        {
            // Already an envelope (Fail helper / validation factory) — idempotent no-op.
            case ObjectResult { Value: ErrorEnvelope }:
            case ObjectResult { Value: SuccessEnvelope }:
                return null;

            // Paginated list marker → success envelope carrying meta.
            case ObjectResult { Value: PagedResult pr } paged:
                return new ObjectResult(Envelope.Success(pr.Data, new MetaInfo(pr.Total, pr.Limit, pr.Offset)))
                { StatusCode = paged.StatusCode };

            // 201 with a Location. CreatedResult is an ObjectResult, so the generic case below would
            // rebuild it as a plain ObjectResult and its Location-writing behavior (which happens
            // during its own execution, after this filter) would never run. Rebuild it as a
            // CreatedResult instead, carrying the envelope as the body. Note: CreatedAtActionResult /
            // CreatedAtRouteResult are NOT covered — they compute their Location from IUrlHelper at
            // formatting time and cannot be reconstructed here. Nothing in this template uses them;
            // a fork that does should return Created(uri, value) instead.
            case CreatedResult created:
                return new CreatedResult(created.Location ?? string.Empty, Envelope.Success(created.Value));

            // Any other value-bearing result: 2xx → success, non-2xx → error by status.
            case ObjectResult obj:
            {
                var status = obj.StatusCode ?? StatusCodes.Status200OK;
                return status is >= 200 and < 300
                    ? new ObjectResult(Envelope.Success(obj.Value)) { StatusCode = obj.StatusCode }
                    : new ObjectResult(Envelope.Error(ErrorCodes.ForStatus(status), Message(obj.Value, status)))
                    { StatusCode = status };
            }

            // 204 success — stays bare.
            case NoContentResult:
                return null;

            // Bare error status result (no body), e.g. NotFound() / StatusCode(4xx).
            case StatusCodeResult { StatusCode: >= 400 and < 600 } scr:
                return new ObjectResult(Envelope.Error(ErrorCodes.ForStatus(scr.StatusCode), DefaultMessage(scr.StatusCode)))
                { StatusCode = scr.StatusCode };

            // FileResult / RedirectResult / ChallengeResult / EmptyResult / SignIn / SignOut / bare 2xx → untouched.
            default:
                return null;
        }
    }

    private static string Message(object? value, int status) =>
        value is string s && s.Length > 0 ? s : DefaultMessage(status);

    private static string DefaultMessage(int status) => status switch
    {
        400 => "Bad request.",
        401 => "Authentication required.",
        403 => "Forbidden.",
        404 => "Resource not found.",
        409 => "Conflict.",
        _ => "An error occurred.",
    };
}
