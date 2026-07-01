namespace Struo.Api.Auth;

/// <summary>Guards the OIDC <c>returnUrl</c> against open redirects: only site-relative paths pass.</summary>
public static class LocalRedirect
{
    public static string Sanitize(string? returnUrl, string fallback) =>
        !string.IsNullOrEmpty(returnUrl)
        && returnUrl.StartsWith('/')
        && !returnUrl.StartsWith("//", StringComparison.Ordinal)
        && !returnUrl.StartsWith("/\\", StringComparison.Ordinal)
            ? returnUrl
            : fallback;
}
