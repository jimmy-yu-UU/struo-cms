using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Struo.Application.Configuration;
using Struo.Application.Security;

namespace Struo.Api.Controllers;

/// <summary>Anonymous public bootstrap config for the SPA: whether external OIDC login is available
/// and the deploy-time branding (name + optional logo URL). A dedicated controller keeps the route as
/// <c>api/config</c> (AuthController owns <c>api/auth</c>).</summary>
[ApiController]
[Route("api/config")]
public sealed class ConfigController : ControllerBase
{
    [AllowAnonymous]
    [HttpGet]
    public IActionResult Get(
        [FromServices] IOptions<OidcOptions> oidc,
        [FromServices] IOptions<BrandingOptions> branding)
        => Ok(new
        {
            oidcEnabled = oidc.Value.Enabled,
            brandName = branding.Value.Name,
            brandLogoUrl = branding.Value.LogoUrl,
        });
}
