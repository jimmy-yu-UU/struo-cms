using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using System.Security.Claims;
using Struo.Api.Auth;
using Struo.Application.Security;

namespace Struo.Api.Controllers;

[ApiController]
[Route("api/auth")]
public sealed class AuthController(IAuthService auth) : ControllerBase
{
    public sealed record LoginRequest(string Email, string Password);

    [AllowAnonymous]
    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] LoginRequest body, CancellationToken ct)
    {
        var result = await auth.AuthenticateAsync(body.Email, body.Password, ct);
        if (!result.Succeeded)
            return Struo.Api.Http.ApiResults.Fail(StatusCodes.Status401Unauthorized,
                Struo.Api.Http.ErrorCodes.Unauthorized, "Invalid credentials.");

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
    public IActionResult Me(
        [FromServices] Struo.Application.Security.ICurrentPermissions permissions,
        [FromServices] Struo.Application.Metadata.SchemaService schema)
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

        return Ok(new
        {
            id = User.FindFirstValue(ClaimTypes.NameIdentifier),
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
