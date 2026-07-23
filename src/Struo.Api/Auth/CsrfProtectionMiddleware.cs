using Struo.Api.Http;
using ErrorCodes = Struo.Api.Http.ErrorCodes;

namespace Struo.Api.Auth;

/// <summary>
/// CSRF defense for cookie-authenticated mutations (OWASP "custom request header" method).
/// A state-changing request that carries the session cookie must also carry the
/// <see cref="HeaderName"/> header. A cross-site page cannot attach a custom header to a
/// credentialed request unless the target's CORS policy allows its origin (the browser blocks the
/// pre-flight), so a forged request from an untrusted origin never reaches the controller.
///
/// Exemptions: safe methods (GET/HEAD/OPTIONS/TRACE); Bearer-token requests (no ambient cookie, so
/// not forgeable); and requests with no session cookie (e.g. POST /api/auth/login before a session
/// exists — those are already CORS-guarded by their JSON content type triggering a pre-flight).
///
/// This relies on CORS not being widened to untrusted origins. StruoCMS CORS is allowlist-only and
/// default-off (see <see cref="CorsWiring"/>), so the assumption holds.
/// </summary>
public sealed class CsrfProtectionMiddleware(RequestDelegate next)
{
    public const string HeaderName = "X-Struo-CSRF";

    private static readonly HashSet<string> SafeMethods =
        new(StringComparer.OrdinalIgnoreCase) { "GET", "HEAD", "OPTIONS", "TRACE" };

    public async Task InvokeAsync(HttpContext context)
    {
        if (RequiresCsrfHeader(context.Request))
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            // AUTH-2: this middleware runs before MVC, so EnvelopeResultFilter never sees this
            // response; the camelCase envelope must be written by hand via the shared options
            // (see EnvelopeJsonOptionsHolder) rather than relying on WriteAsJsonAsync's default
            // (non-camelCase) HttpResponseJsonOptions.
            await context.Response.WriteAsJsonAsync(
                Envelope.Error(ErrorCodes.Forbidden, $"Missing required '{HeaderName}' header."),
                EnvelopeJsonOptionsHolder.Instance);
            return;
        }

        await next(context);
    }

    private static bool RequiresCsrfHeader(HttpRequest request)
    {
        if (SafeMethods.Contains(request.Method)) return false;

        // Bearer-authenticated requests carry no ambient browser credential, so they cannot be forged.
        var authorization = request.Headers.Authorization.ToString();
        if (authorization.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)) return false;

        // Only requests riding on the session cookie need the guard.
        if (!request.Cookies.ContainsKey(AuthSchemes.SessionCookieName)) return false;

        return !request.Headers.ContainsKey(HeaderName);
    }
}
