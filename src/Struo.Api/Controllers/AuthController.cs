using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;
using System.Globalization;
using System.Security.Claims;
using Struo.Api.Auth;
using Struo.Application.Security;

namespace Struo.Api.Controllers;

[ApiController]
[Route("api/auth")]
public sealed class AuthController(
    IAuthService auth, ILoginAttemptThrottle loginThrottle, SessionRotation sessionRotation,
    SessionCookieName sessionCookie, ILogger<AuthController> logger) : ControllerBase
{
    public sealed record LoginRequest(string Email, string Password);

    // Three independent login defenses guard this action:
    // 1. The per-client-IP fixed-window limiter ([EnableRateLimiting("login")], see StruoCmsRateLimiting) —
    //    ships DISABLED by default (LoginRateLimitOptions).
    // 2. loginThrottle — a per-ACCOUNT throttle that ships ENABLED by default, since it cannot suffer
    //    the per-IP layer's shared-egress collapse. It counts the attempt before the password is
    //    verified, so parallel guesses cannot all slip past the limit.
    // 3. A process-wide cap on concurrent logins (LoginConcurrencyOptions, StruoCmsRateLimiting).
    // Each attempt burns full Argon2id CPU, making unbounded brute-forcing a DoS amplifier. Applied to
    // this action only — logout/me/oidc below are intentionally NOT limited by any layer.
    [AllowAnonymous]
    [EnableRateLimiting("login")]
    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] LoginRequest body, CancellationToken ct)
    {
        // Reserved BEFORE AuthenticateAsync so a throttled account spends no Argon2id CPU at all, and
        // so the attempt is already counted while the password is being verified.
        var throttleState = await loginThrottle.TryReserveAsync(body.Email, ct);
        if (throttleState.IsBlocked)
        {
            Response.Headers.RetryAfter = throttleState.RetryAfterSeconds.ToString(CultureInfo.InvariantCulture);
            // Same status, code and message the per-client-IP limiter's OnRejected callback writes
            // (StruoCmsRateLimiting) — the two layers are deliberately indistinguishable to a client.
            return Struo.Api.Http.ApiResults.Fail(StatusCodes.Status429TooManyRequests,
                Struo.Api.Http.ErrorCodes.TooManyRequests, "Too many login attempts. Please try again later.");
        }

        var result = await auth.AuthenticateAsync(body.Email, body.Password, ct);
        if (!result.Succeeded)
        {
            // Every failure stays counted, regardless of AuthFailure kind — deliberately not
            // special-casing Inactive: exempting it would make "this email never trips the throttle"
            // an oracle for "password correct, account disabled", reopening exactly the enumeration
            // surface the shared InvalidCredentials code below exists to close.

            // Inactive is only ever returned when the password verified (AuthService checks the hash
            // first), so it gets its own code. InvalidCredentials deliberately covers BOTH "wrong
            // password" and "no such account" under one code — that pair must stay indistinguishable.
            return result.Failure == Struo.Application.Security.AuthFailure.Inactive
                ? Struo.Api.Http.ApiResults.Fail(StatusCodes.Status401Unauthorized,
                    Struo.Api.Http.ErrorCodes.AccountInactive, "This account has been deactivated.")
                : InvalidCredentials();
        }

        var identity = new ClaimsIdentity(AuthSchemes.Cookie);
        identity.AddClaim(new Claim(ClaimTypes.NameIdentifier, result.UserId!.Value.ToString()));
        await HttpContext.SignInAsync(AuthSchemes.Cookie, new ClaimsPrincipal(identity));

        // A password change or removal that revoked sessions before this one was written has not
        // covered it. If the stored credential differs from the one verified above, the session is
        // revoked here and the login fails like a wrong password. The reserved attempt stays counted,
        // as for any failed login. A step that throws discards the session too, so a failed login
        // never leaves one behind; the step's own exception is the one that propagates.
        bool confirmed;
        try
        {
            confirmed = await FinishLoginAsync(result, body.Email);
        }
        catch
        {
            await TryDiscardNewSessionAsync();
            throw;
        }

        if (!confirmed)
        {
            await TryDiscardNewSessionAsync();
            return InvalidCredentials();
        }

        return Ok(new { id = result.UserId });
    }

    // The steps after the session is written do not take the request token: once it exists they must
    // complete even if the client has gone, or the session would outlive a failed login.
    private async Task<bool> FinishLoginAsync(AuthResult result, string email)
    {
        if (!await auth.ConfirmUnchangedAsync(result, CancellationToken.None)) return false;

        // A legitimate login clears the reserved attempts, so a real user can never lock
        // themselves out by logging in normally.
        await loginThrottle.ResetAsync(email, CancellationToken.None);
        await sessionRotation.RevokePresentedAsync(HttpContext);
        return true;
    }

    private static ObjectResult InvalidCredentials() =>
        Struo.Api.Http.ApiResults.Fail(StatusCodes.Status401Unauthorized,
            Struo.Api.Http.ErrorCodes.Unauthorized, "Invalid credentials.");

    // Sign-out removes the ticket and its index row through the ticket store; the session cookie (and
    // its chunks) the sign-in and the sign-out queued are dropped so the client receives neither.
    // Any other cookie on the response stays. A sign-out that fails is logged and does not replace the
    // outcome of the login: the client still gets no session cookie, so the ticket it could not reach
    // expires with its sliding window.
    private async Task TryDiscardNewSessionAsync()
    {
        try
        {
            await HttpContext.SignOutAsync(AuthSchemes.Cookie);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Discarding the session of a failed login did not complete.");
        }
        finally
        {
            Response.Headers.SetCookie = SessionCookieHeaders.Without(Response.Headers.SetCookie, sessionCookie.Value);
        }
    }

    [Authorize(AuthenticationSchemes = AuthSchemes.CookieOrBearer)]
    [HttpPost("logout")]
    public async Task<IActionResult> Logout()
    {
        await HttpContext.SignOutAsync(AuthSchemes.Cookie);
        return NoContent();
    }

    [Authorize(AuthenticationSchemes = AuthSchemes.CookieOrBearer)]
    [HttpGet("me")]
    public async Task<IActionResult> Me(
        [FromServices] Struo.Application.Security.ICurrentPermissions permissions,
        [FromServices] Struo.Application.Metadata.SchemaService schema,
        [FromServices] IUserAccountStore accounts,
        CancellationToken ct)
    {
        var eff = permissions.Current;
        var map = PermissionMap.Build(eff, schema);

        var uid = User.FindFirstValue(ClaimTypes.NameIdentifier);
        string? email = null;
        string? name = null;
        if (Guid.TryParse(uid, out var userId))
        {
            var profile = await accounts.FindProfileAsync(userId, ct);
            email = profile?.Email;
            name = profile?.Name;
        }

        return Ok(new
        {
            id = uid,
            email,
            name,
            isSuperAdmin = eff.IsSuperAdmin,
            permissions = map
        });
    }

    [AllowAnonymous]
    [HttpGet("login/oidc/{provider}")]
    public IActionResult LoginOidc(string provider, [FromQuery] string? returnUrl, [FromServices] IOptions<OidcOptions> oidc,
        [FromServices] IConfiguration config)
    {
        if (!oidc.Value.TryGetEnabled(provider, out _))
            return NotFound();

        var target = Struo.Api.Auth.LocalRedirect.Sanitize(
            returnUrl, oidc.Value.ReturnUrlDefault, Struo.Api.Auth.CorsWiring.ConfiguredOrigins(config));
        return Challenge(new AuthenticationProperties { RedirectUri = target }, AuthSchemes.OidcFor(provider));
    }
}
