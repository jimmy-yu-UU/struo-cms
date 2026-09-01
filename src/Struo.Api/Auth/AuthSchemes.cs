using Microsoft.AspNetCore.Http;

namespace Struo.Api.Auth;

public static class AuthSchemes
{
    public const string Cookie = "Cookies";
    public const string Bearer = "Bearer";
    public const string CookieOrBearer = "Cookies,Bearer";
    public const string Oidc = "oidc";

    /// <summary>Forwarding policy scheme registered as the DEFAULT: it forwards to
    /// <see cref="Bearer"/> when the request carries an <c>Authorization: Bearer …</c> header and to
    /// <see cref="Cookie"/> otherwise. Without it, UseAuthentication only ran the cookie handler, so
    /// endpoints with no <c>[Authorize]</c> (item reads, <c>/graphql</c>) never inspected a token.</summary>
    public const string Adaptive = "Adaptive";

    /// <summary>Name of the cookie holding the auth session. Shared by the cookie handler
    /// (AuthWiring) and the CSRF middleware (which keys its guard off this cookie's presence).</summary>
    public const string SessionCookieName = "struo.session";

    /// <summary>
    /// Single source of truth for how long a cookie session lives: both
    /// <c>CookieAuthenticationOptions.ExpireTimeSpan</c> (<c>AuthWiring</c>, the sliding window the
    /// cookie handler itself enforces) and <c>DistributedCacheTicketStore</c>'s own idea of a
    /// session's lifetime (the cache entry's sliding expiration, and what it stamps into a
    /// <c>UserSession</c> row's <c>ExpiresAt</c>) MUST agree. The login-time index sweep
    /// (<c>DistributedCacheTicketStore.IndexNewSessionAsync</c>) deletes a user's own index rows past
    /// their <c>ExpiresAt</c> — that is only safe when this value really is how long the cookie stays
    /// alive; if the two ever diverged (e.g. a fork raising one but not the other), a still-live
    /// cookie's index row could be swept out from under it, making that session permanently
    /// un-revocable (no row left to find it by) while the cookie itself keeps working.
    /// </summary>
    public static readonly TimeSpan SessionLifetime = TimeSpan.FromHours(8);

    /// <summary>Does this request carry an <c>Authorization: Bearer …</c> header? The single
    /// definition of "this is a bearer request" shared by the <see cref="Adaptive"/> forwarding
    /// selector (<c>AuthWiring</c>), <c>BearerTokenAuthenticationHandler</c>, and
    /// <c>CsrfProtectionMiddleware</c>'s CSRF exemption. All three MUST agree: if the authentication
    /// selector and the CSRF exemption ever diverge, the result is either a CSRF bypass or an
    /// authentication hole.</summary>
    internal static bool HasBearerHeader(HttpRequest request) =>
        request.Headers.Authorization.ToString().StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase);
}
