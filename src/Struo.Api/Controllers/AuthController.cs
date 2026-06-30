using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
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
            return Unauthorized(new { error = new { message = "Invalid credentials." } });

        var identity = new ClaimsIdentity(AuthSchemes.Cookie);
        identity.AddClaim(new Claim(ClaimTypes.NameIdentifier, result.UserId!.Value.ToString()));
        await HttpContext.SignInAsync(AuthSchemes.Cookie, new ClaimsPrincipal(identity));
        return Ok(new { data = new { id = result.UserId } });
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
    public IActionResult Me() =>
        Ok(new { data = new { id = User.FindFirstValue(ClaimTypes.NameIdentifier) } });
}
