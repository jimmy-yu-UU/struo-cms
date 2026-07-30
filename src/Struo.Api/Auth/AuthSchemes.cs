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
}
