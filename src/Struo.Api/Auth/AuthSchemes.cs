namespace Struo.Api.Auth;

public static class AuthSchemes
{
    public const string Cookie = "Cookies";
    public const string Bearer = "Bearer";
    public const string CookieOrBearer = "Cookies,Bearer";
    public const string Oidc = "oidc";

    /// <summary>Name of the cookie holding the auth session. Shared by the cookie handler
    /// (AuthWiring) and the CSRF middleware (which keys its guard off this cookie's presence).</summary>
    public const string SessionCookieName = "struo.session";
}
