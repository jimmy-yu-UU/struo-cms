using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using Struo.Application.Configuration;
using Struo.Application.Security;
using Struo.Application.Settings;
using FileService = Struo.Infrastructure.Files.FileService;

namespace Struo.Api.Controllers;

/// <summary>Anonymous public bootstrap config for the SPA: whether external OIDC login is available
/// and the effective branding (DB singleton overrides the deploy-time appsettings defaults,
/// field-by-field). A dedicated controller keeps the route as <c>api/config</c>.</summary>
[ApiController]
[Route("api/config")]
public sealed class ConfigController(IMemoryCache cache) : ControllerBase
{
    /// <summary>Shared with <see cref="SettingsController"/>, which evicts this key immediately
    /// after a successful branding save so the 30s TTL below never delays a deliberate change.</summary>
    public const string CacheKey = "struo:config:effective";

    private static readonly TimeSpan CacheDuration = TimeSpan.FromSeconds(30);

    [AllowAnonymous]
    [HttpGet]
    public async Task<IActionResult> Get(
        [FromServices] IOptions<OidcOptions> oidc,
        [FromServices] IOptions<BrandingOptions> branding,
        [FromServices] ISiteSettingsStore settings,
        [FromServices] FileService files,
        [FromServices] IOptions<PasswordPolicyOptions> passwordPolicy,
        [FromServices] IOptions<AdminUiOptions> adminUi,
        CancellationToken ct)
    {
        // This endpoint is anonymous and was hitting the DB on every request. 30s staleness
        // is acceptable for branding/oidc bootstrap data; UpdateBranding evicts this key on save so
        // a deliberate change is reflected immediately rather than after the TTL.
        if (cache.TryGetValue(CacheKey, out object? cached))
            return Ok(cached);

        var saved = await settings.GetAsync(ct);
        var name = saved is not null && !string.IsNullOrWhiteSpace(saved.BrandName)
            ? saved.BrandName
            : branding.Value.Name;

        // SettingsController only checks the file is published at SAVE time. If it
        // was later unpublished or deleted, keep serving the appsettings default instead of a dead
        // /api/files/{id}/content URL on the anonymous login page.
        var logoUrl = branding.Value.LogoUrl;
        if (saved?.LogoFileId is { } id)
        {
            var file = await files.GetAsync(id, ct);
            if (file is not null && file.Status == "published")
                logoUrl = $"/api/files/{id}/content";
        }

        // passwordMinLength is startup-bound config (not DB state), so it is safe under the 30s
        // cache below — a change requires a restart anyway. Publishing it anonymously is fine: a
        // minimum length is discoverable by trying, and the SPA needs it before anyone signs in.
        var payload = new
        {
            oidcEnabled = oidc.Value.Enabled,
            brandName = name,
            brandLogoUrl = logoUrl,
            passwordMinLength = passwordPolicy.Value.MinLength,
            // Startup-bound like passwordMinLength; the 30s cache is irrelevant to it.
            uiLocales = adminUi.Value.EffectiveLocales,
            uiDefaultLocale = adminUi.Value.DefaultLocale
        };
        cache.Set(CacheKey, payload, CacheDuration);
        return Ok(payload);
    }
}
