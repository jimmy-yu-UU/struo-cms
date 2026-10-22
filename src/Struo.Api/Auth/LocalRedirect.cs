namespace Struo.Api.Auth;

/// <summary>Guards the OIDC <c>returnUrl</c> against open redirects: only site-relative paths pass.</summary>
public static class LocalRedirect
{
    // Browsers strip tab, CR and LF from a URL, so "/\t/host" would be read as "//host"; any
    // control character is refused.
    public static string Sanitize(string? returnUrl, string fallback) =>
        !string.IsNullOrEmpty(returnUrl)
        && returnUrl.StartsWith('/')
        && !returnUrl.StartsWith("//", StringComparison.Ordinal)
        && !returnUrl.StartsWith("/\\", StringComparison.Ordinal)
        && !returnUrl.Any(char.IsControl)
            ? returnUrl
            : fallback;
}
