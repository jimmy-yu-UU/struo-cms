using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.Extensions.Options;

namespace Struo.Api.Auth;

/// <summary>
/// Keeps a sign-in from ever re-using the session key of a cookie the request arrived with.
/// <c>CookieAuthenticationHandler</c> renews the key it read from the request cookie when it signs in
/// (<see cref="Microsoft.AspNetCore.Authentication.Cookies.ITicketStore.RenewAsync"/>), so a cookie planted
/// by an attacker would become the victim's session. <see cref="SessionRotationMiddleware"/> hides that
/// cookie from the handler on sign-in requests; this class then revokes the hidden session once the
/// sign-in succeeded.
/// </summary>
public sealed class SessionRotation(
    IOptionsMonitor<CookieAuthenticationOptions> cookieOptions, DistributedCacheTicketStore store)
{
    // Claim the cookie handler stores the ticket-store key under; part of the cookie's wire format.
    private const string SessionIdClaimType = "Microsoft.AspNetCore.Authentication.Cookies-SessionId";
    private const string PresentedCookieItem = "Struo.PresentedSessionCookie";

    /// <summary>The session cookie value the request carried before it was hidden, if any.</summary>
    public static string? PresentedCookie(HttpContext context) =>
        context.Items.TryGetValue(PresentedCookieItem, out var v) ? v as string : null;

    internal static void RecordPresentedCookie(HttpContext context, string value) =>
        context.Items[PresentedCookieItem] = value;

    /// <summary>Removes the server-side session the request's cookie pointed at. A cookie that does not
    /// unprotect or carries no key is ignored.</summary>
    public async Task RevokePresentedAsync(HttpContext context)
    {
        if (PresentedCookie(context) is not { } value) return;
        var options = cookieOptions.Get(AuthSchemes.Cookie);
        var key = options.TicketDataFormat.Unprotect(value)?.Principal.FindFirst(SessionIdClaimType)?.Value;
        if (key is not null) await store.RemoveAsync(key);
    }
}
