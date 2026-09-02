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
public sealed class AuthController(IAuthService auth, ILoginAttemptThrottle loginThrottle) : ControllerBase
{
    public sealed record LoginRequest(string Email, string Password);

    // TWO independent login defenses guard this action, not one:
    // 1. The per-client-IP fixed-window limiter below ([EnableRateLimiting("login")], see
    //    Program.cs) — ships DISABLED by default (LoginRateLimitOptions).
    // 2. loginThrottle, checked first thing in the method body below — a per-ACCOUNT throttle that
    //    ships ENABLED by default, since it cannot suffer the per-IP layer's shared-egress collapse.
    // Both exist because every anonymous attempt burns full Argon2id CPU, making unbounded
    // brute-forcing a DoS amplifier. Applied to this action only — logout/me/oidc below are
    // intentionally NOT limited by either layer.
    [AllowAnonymous]
    [EnableRateLimiting("login")]
    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] LoginRequest body, CancellationToken ct)
    {
        // Checked BEFORE AuthenticateAsync so a throttled account spends no Argon2id CPU at all —
        // the whole point of throttling ahead of the hash verify rather than after it.
        var throttleState = await loginThrottle.CheckAsync(body.Email, ct);
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
            // Every failure counts, regardless of AuthFailure kind — deliberately not special-casing
            // Inactive: exempting it would make "this email never trips the throttle" an oracle for
            // "password correct, account disabled", reopening exactly the enumeration surface the
            // shared InvalidCredentials code below exists to close.
            await loginThrottle.RecordFailureAsync(body.Email, ct);

            // Inactive is only ever returned when the password verified (AuthService checks the hash
            // first), so it gets its own code. InvalidCredentials deliberately covers BOTH "wrong
            // password" and "no such account" under one code — that pair must stay indistinguishable.
            return result.Failure == Struo.Application.Security.AuthFailure.Inactive
                ? Struo.Api.Http.ApiResults.Fail(StatusCodes.Status401Unauthorized,
                    Struo.Api.Http.ErrorCodes.AccountInactive, "This account has been deactivated.")
                : Struo.Api.Http.ApiResults.Fail(StatusCodes.Status401Unauthorized,
                    Struo.Api.Http.ErrorCodes.Unauthorized, "Invalid credentials.");
        }

        // A legitimate login clears any accumulated failures, so a real user can never lock
        // themselves out by logging in normally.
        await loginThrottle.ResetAsync(body.Email, ct);

        var identity = new ClaimsIdentity(AuthSchemes.Cookie);
        identity.AddClaim(new Claim(ClaimTypes.NameIdentifier, result.UserId!.Value.ToString()));
        await HttpContext.SignInAsync(AuthSchemes.Cookie, new ClaimsPrincipal(identity));
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
        var map = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
        if (!eff.IsSuperAdmin)
        {
            foreach (var c in schema.GetAll())
            {
                var read = eff.CanRead(c.Name);
                var write = eff.CanWrite(c.Name);
                var del = eff.CanDelete(c.Name);
                if (read || write || del)
                    map[c.Name] = new { read, write, @delete = del };
            }
        }

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
    [HttpGet("login/oidc")]
    public IActionResult LoginOidc([FromQuery] string? returnUrl, [FromServices] IOptions<OidcOptions> oidc)
    {
        if (!oidc.Value.Enabled)
            return NotFound();

        var target = Struo.Api.Auth.LocalRedirect.Sanitize(returnUrl, oidc.Value.ReturnUrlDefault);
        return Challenge(new AuthenticationProperties { RedirectUri = target }, AuthSchemes.Oidc);
    }
}
