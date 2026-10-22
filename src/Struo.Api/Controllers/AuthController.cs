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
    IAuthService auth, ILoginAttemptThrottle loginThrottle, SessionRotation sessionRotation) : ControllerBase
{
    public sealed record LoginRequest(string Email, string Password);

    // Three independent login defenses guard this action:
    // 1. The per-client-IP fixed-window limiter ([EnableRateLimiting("login")], see Program.cs) —
    //    ships DISABLED by default (LoginRateLimitOptions).
    // 2. loginThrottle — a per-ACCOUNT throttle that ships ENABLED by default, since it cannot suffer
    //    the per-IP layer's shared-egress collapse. It counts the attempt before the password is
    //    verified, so parallel guesses cannot all slip past the limit.
    // 3. A process-wide cap on concurrent logins (LoginConcurrencyOptions, Program.cs).
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
            // (Program.cs) — the two layers are deliberately indistinguishable to a client.
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
                : Struo.Api.Http.ApiResults.Fail(StatusCodes.Status401Unauthorized,
                    Struo.Api.Http.ErrorCodes.Unauthorized, "Invalid credentials.");
        }

        // A legitimate login clears the reserved attempts, so a real user can never lock
        // themselves out by logging in normally.
        await loginThrottle.ResetAsync(body.Email, ct);

        var identity = new ClaimsIdentity(AuthSchemes.Cookie);
        identity.AddClaim(new Claim(ClaimTypes.NameIdentifier, result.UserId!.Value.ToString()));
        await HttpContext.SignInAsync(AuthSchemes.Cookie, new ClaimsPrincipal(identity));
        await sessionRotation.RevokePresentedAsync(HttpContext);
        return Ok(new { id = result.UserId });
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
    public IActionResult LoginOidc(string provider, [FromQuery] string? returnUrl, [FromServices] IOptions<OidcOptions> oidc)
    {
        if (!oidc.Value.TryGetEnabled(provider, out _))
            return NotFound();

        var target = Struo.Api.Auth.LocalRedirect.Sanitize(returnUrl, oidc.Value.ReturnUrlDefault);
        return Challenge(new AuthenticationProperties { RedirectUri = target }, AuthSchemes.OidcFor(provider));
    }
}
