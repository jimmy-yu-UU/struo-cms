using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Struo.Application.Configuration;
using Struo.Application.Security;
using Struo.Application.Settings;

namespace Struo.Api.Controllers;

/// <summary>Anonymous public bootstrap config for the SPA: whether external OIDC login is available
/// and the effective branding (DB singleton overrides the deploy-time appsettings defaults,
/// field-by-field). A dedicated controller keeps the route as <c>api/config</c>.</summary>
[ApiController]
[Route("api/config")]
public sealed class ConfigController : ControllerBase
{
    [AllowAnonymous]
    [HttpGet]
    public async Task<IActionResult> Get(
        [FromServices] IOptions<OidcOptions> oidc,
        [FromServices] IOptions<BrandingOptions> branding,
        [FromServices] ISiteSettingsStore settings,
        CancellationToken ct)
    {
        var saved = await settings.GetAsync(ct);
        var name = saved is not null && !string.IsNullOrWhiteSpace(saved.BrandName)
            ? saved.BrandName
            : branding.Value.Name;
        var logoUrl = saved?.LogoFileId is { } id
            ? $"/api/files/{id}/content"
            : branding.Value.LogoUrl;
        return Ok(new { oidcEnabled = oidc.Value.Enabled, brandName = name, brandLogoUrl = logoUrl });
    }
}
