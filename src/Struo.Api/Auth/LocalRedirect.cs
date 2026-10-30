namespace Struo.Api.Auth;

/// <summary>Guards post-sign-in destinations against open redirects. A destination is either a
/// site-relative path or an absolute http(s) URL whose origin (scheme, host, port) equals one of the
/// configured <c>Struo:Cors:AllowedOrigins</c>. With no origin configured only relative paths pass.</summary>
public static class LocalRedirect
{
    public static string Sanitize(string? returnUrl, string fallback, IReadOnlyCollection<string>? allowedOrigins = null) =>
        IsValidDefault(returnUrl, allowedOrigins) ? returnUrl! : fallback;

    /// <summary>The normalized origin of <paramref name="url"/> when it is an absolute URL on an allowed
    /// origin; otherwise null.</summary>
    public static string? AllowedOrigin(string? url, IReadOnlyCollection<string>? allowedOrigins)
    {
        if (allowedOrigins is not { Count: > 0 } || !TryGetOrigin(url, requireOriginOnly: false, out var origin))
            return null;
        return allowedOrigins.Any(a => TryGetOrigin(a, requireOriginOnly: true, out var o) && o == origin)
            ? origin
            : null;
    }

    /// <summary>Whether <paramref name="value"/> is an acceptable sign-in destination: a relative path,
    /// or an absolute URL on an allowed origin.</summary>
    public static bool IsValidDefault(string? value, IReadOnlyCollection<string>? allowedOrigins) =>
        IsRelative(value) || AllowedOrigin(value, allowedOrigins) is not null;

    /// <summary>The login page a failed sign-in ends on. With origins configured it is on the origin of
    /// the approved <paramref name="returnUrl"/>, else of an absolute <paramref name="defaultUrl"/>;
    /// otherwise it is the relative <c>/login</c>.</summary>
    public static string LoginFailureUrl(string code, string? returnUrl, string? defaultUrl,
        IReadOnlyCollection<string>? allowedOrigins)
    {
        var origin = AllowedOrigin(returnUrl, allowedOrigins) ?? AllowedOrigin(defaultUrl, allowedOrigins);
        return $"{origin}/login?error={Uri.EscapeDataString(code)}";
    }

    // Browsers strip tab, CR and LF from a URL, so "/\t/host" would be read as "//host"; any
    // control character is refused.
    private static bool IsRelative(string? url) =>
        !string.IsNullOrEmpty(url)
        && url.StartsWith('/')
        && !url.StartsWith("//", StringComparison.Ordinal)
        && !url.StartsWith("/\\", StringComparison.Ordinal)
        && !url.Any(char.IsControl);

    // Requiring the literal "http(s)://" prefix and refusing backslashes, whitespace, control
    // characters and userinfo keeps the parser from accepting what a browser would read differently.
    private static bool TryGetOrigin(string? url, bool requireOriginOnly, out string origin)
    {
        origin = "";
        if (string.IsNullOrEmpty(url)
            || url.Any(c => char.IsControl(c) || char.IsWhiteSpace(c) || c == '\\')
            || !(url.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                 || url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            || !Uri.TryCreate(url, UriKind.Absolute, out var uri)
            || uri.UserInfo.Length > 0
            || uri.Host.Length == 0
            || uri.Host.Contains('*'))
            return false;
        if (requireOriginOnly && (uri.PathAndQuery != "/" || uri.Fragment.Length > 0))
            return false;
        origin = uri.GetLeftPart(UriPartial.Authority).ToLowerInvariant();
        return true;
    }
}
