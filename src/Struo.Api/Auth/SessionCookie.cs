// src/Struo.Api/Auth/SessionCookie.cs
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.Extensions.Options;

namespace Struo.Api.Auth;

/// <summary>The configured name of the session cookie. Read per request from the effective cookie options, so a fork's own changes to them count.</summary>
public sealed class SessionCookieName(Func<string> resolve)
{
    /// <summary>Config key that renames the session cookie.</summary>
    public const string ConfigKey = "Auth:Cookie:Name";

    /// <summary>The name used when <see cref="ConfigKey"/> is unset or blank.</summary>
    public const string Default = "struo.session";

    /// <summary>The current cookie name.</summary>
    public string Value => resolve();

    /// <summary>A fixed name, for tests that build a middleware directly.</summary>
    public static SessionCookieName Fixed(string name = Default) => new(() => name);

    internal static string FromConfiguration(IConfiguration config) =>
        config[ConfigKey] is { } name && !string.IsNullOrWhiteSpace(name) ? name : Default;
}

/// <summary>
/// Fails startup when the session cookie's effective options cannot carry its name: the name must be a valid
/// cookie token, a <c>__Secure-</c> name needs a Secure cookie, and a <c>__Host-</c> name also needs
/// <c>Path=/</c> and no <c>Domain</c>. Browsers silently drop a cookie that breaks its prefix rule, which
/// would sign nobody in.
/// </summary>
internal sealed partial class SessionCookieOptionsValidator : IValidateOptions<CookieAuthenticationOptions>
{
    private const string HostPrefix = "__Host-";
    private const string SecurePrefix = "__Secure-";

    // RFC 6265 cookie-name is an RFC 2616 token.
    [GeneratedRegex("^[!#$%&'*+\\-.^_`|~0-9A-Za-z]+\\z")]
    private static partial Regex TokenPattern();

    public ValidateOptionsResult Validate(string? name, CookieAuthenticationOptions options)
    {
        if (name != AuthSchemes.Cookie) return ValidateOptionsResult.Skip;

        var cookie = options.Cookie;
        var cookieName = cookie.Name ?? "";
        if (!TokenPattern().IsMatch(cookieName))
            return ValidateOptionsResult.Fail(
                $"{SessionCookieName.ConfigKey} ('{cookieName}') is not a valid cookie name: use letters, digits and these characters only: ! # $ % & ' * + - . ^ _ ` | ~");

        var hostPrefixed = cookieName.StartsWith(HostPrefix, StringComparison.OrdinalIgnoreCase);
        var securePrefixed = hostPrefixed || cookieName.StartsWith(SecurePrefix, StringComparison.OrdinalIgnoreCase);
        if (securePrefixed && cookie.SecurePolicy != CookieSecurePolicy.Always)
            return ValidateOptionsResult.Fail(
                $"{SessionCookieName.ConfigKey} ('{cookieName}') needs a Secure cookie, but the cookie is not always Secure: set Auth:Cookie:RequireSecure to true.");
        if (hostPrefixed && (!string.IsNullOrEmpty(cookie.Domain) || !(cookie.Path is null or "/")))
            return ValidateOptionsResult.Fail(
                $"{SessionCookieName.ConfigKey} ('{cookieName}') starts with {HostPrefix}, which needs Path=/ and no Domain on the cookie.");

        return ValidateOptionsResult.Success;
    }
}
