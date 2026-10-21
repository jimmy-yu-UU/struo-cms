using Microsoft.Extensions.Options;
using Struo.Application.Security;

namespace Struo.Api.Auth;

/// <summary>
/// On the sign-in requests (password login, OIDC callback) hides the session cookie from the rest of the
/// pipeline, so the cookie handler issues a fresh session key instead of renewing the presented one.
/// The presented cookie is remembered for <see cref="SessionRotation.RevokePresentedAsync"/>. Must sit
/// ahead of <c>UseAuthentication</c>.
/// </summary>
public sealed class SessionRotationMiddleware(RequestDelegate next, IOptions<OidcOptions> oidc)
{
    public const string LoginPath = "/api/auth/login";

    public Task InvokeAsync(HttpContext context)
    {
        if (IsSignInRequest(context.Request)) HidePresentedCookie(context.Request, context);
        return next(context);
    }

    private bool IsSignInRequest(HttpRequest request)
    {
        var path = (request.Path.Value ?? "").TrimEnd('/');
        if (HttpMethods.IsPost(request.Method) && path.Equals(LoginPath, StringComparison.OrdinalIgnoreCase))
            return true;
        return oidc.Value.EnabledProviders().Any(p =>
            path.Equals(p.Options.EffectiveCallbackPath(p.Id).TrimEnd('/'), StringComparison.OrdinalIgnoreCase));
    }

    private static void HidePresentedCookie(HttpRequest request, HttpContext context)
    {
        var prefix = AuthSchemes.SessionCookieName + "=";
        var kept = new List<string>();
        foreach (var header in request.Headers.Cookie)
        {
            var pairs = (header ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            foreach (var pair in pairs)
            {
                if (pair.StartsWith(prefix, StringComparison.Ordinal))
                    SessionRotation.RecordPresentedCookie(context, pair[prefix.Length..]);
                else
                    kept.Add(pair);
            }
        }
        if (SessionRotation.PresentedCookie(context) is null) return;
        if (kept.Count == 0) request.Headers.Remove("Cookie");
        else request.Headers.Cookie = string.Join("; ", kept);
    }
}
