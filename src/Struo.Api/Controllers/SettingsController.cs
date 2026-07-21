using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Struo.Api.Auth;
using Struo.Api.Http;
using ErrorCodes = Struo.Api.Http.ErrorCodes;
using Struo.Application.Abstractions;
using Struo.Application.Configuration;
using Struo.Application.Security;
using Struo.Application.Settings;
using FileService = Struo.Infrastructure.Files.FileService;

namespace Struo.Api.Controllers;

public sealed record UpdateBrandingRequest(string? BrandName, Guid? LogoFileId);

/// <summary>Super-admin site settings. Writes go to the singleton <c>site_settings</c> row that
/// <see cref="ConfigController"/> then reflects to every SPA. Cookie writes require the
/// <c>X-Struo-CSRF</c> header (enforced globally by CsrfProtectionMiddleware).</summary>
[ApiController]
[Route("api/settings")]
[Authorize(AuthenticationSchemes = AuthSchemes.CookieOrBearer)]
public sealed class SettingsController(
    ISiteSettingsStore settings, FileService files, IOptions<BrandingOptions> branding,
    ICurrentPermissions permissions, ICurrentUserAccessor currentUser) : ControllerBase
{
    private const int MaxBrandNameLength = 100;

    [HttpPut("branding")]
    public async Task<IActionResult> UpdateBranding([FromBody] UpdateBrandingRequest body, CancellationToken ct)
    {
        if (!permissions.Current.IsSuperAdmin)
            return ApiResults.Fail(StatusCodes.Status403Forbidden, ErrorCodes.Forbidden, "Admin role required.");

        var name = body.BrandName?.Trim() ?? "";
        if (name.Length == 0)
            return ApiResults.Fail(StatusCodes.Status400BadRequest, ErrorCodes.BadUserInput, "Brand name is required.");
        if (name.Length > MaxBrandNameLength)
            return ApiResults.Fail(StatusCodes.Status400BadRequest, ErrorCodes.BadUserInput,
                $"Brand name must be at most {MaxBrandNameLength} characters.");

        if (body.LogoFileId is { } fileId)
        {
            var file = await files.GetAsync(fileId, ct);
            if (file is null || file.Status != "published")
                return ApiResults.Fail(StatusCodes.Status400BadRequest, ErrorCodes.BadUserInput,
                    "Logo file not found or not published.");
        }

        await settings.UpsertAsync(name, body.LogoFileId, currentUser.GetCurrentUserId(), ct);

        var logoUrl = body.LogoFileId is { } id ? $"/api/files/{id}/content" : branding.Value.LogoUrl;
        return Ok(new { brandName = name, brandLogoUrl = logoUrl });
    }
}
