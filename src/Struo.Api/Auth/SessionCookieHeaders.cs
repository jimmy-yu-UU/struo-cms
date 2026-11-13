using System.Text.RegularExpressions;
using Microsoft.Extensions.Primitives;

namespace Struo.Api.Auth;

/// <summary>
/// Picks the session cookie's <c>Set-Cookie</c> values out of a response. The cookie handler may split a
/// large ticket over several cookies: <c>ChunkingCookieManager</c> writes the base cookie with the value
/// <c>chunks-N</c> and the parts as <c>{name}C1</c>, <c>{name}C2</c> and so on.
/// </summary>
internal static partial class SessionCookieHeaders
{
    /// <summary>The values of <paramref name="setCookie"/> that do not set the session cookie or one of its chunks.</summary>
    internal static StringValues Without(StringValues setCookie, string sessionCookieName)
    {
        var escaped = Uri.EscapeDataString(sessionCookieName);
        return new StringValues(setCookie.OfType<string>().Where(value => !SetsSessionCookie(value, escaped)).ToArray());
    }

    private static bool SetsSessionCookie(string headerValue, string escapedName)
    {
        var end = headerValue.IndexOf('=', StringComparison.Ordinal);
        var cookieName = (end < 0 ? headerValue : headerValue[..end]).Trim();
        return cookieName == escapedName
            || (cookieName.StartsWith(escapedName, StringComparison.Ordinal)
                && ChunkSuffix().IsMatch(cookieName.AsSpan(escapedName.Length)));
    }

    [GeneratedRegex(@"^C\d+$")]
    private static partial Regex ChunkSuffix();
}
