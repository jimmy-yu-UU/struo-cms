using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Struo.Api.Auth;
using Struo.Api.Http;
using ErrorCodes = Struo.Api.Http.ErrorCodes; // disambiguates from HotChocolate.ErrorCodes
using Struo.Application.Security;

namespace Struo.Api.Controllers;

/// <summary>Wire DTO for one system capability a role can be given.</summary>
public sealed record CapabilityEntry(string Code, string LabelKey, string DescriptionKey);

[ApiController]
[Route("api/capabilities")]
[Authorize(AuthenticationSchemes = AuthSchemes.CookieOrBearer)]
public sealed class CapabilitiesController(SystemCapabilityCatalog catalog, ICurrentPermissions permissions) : ControllerBase
{
    /// <summary>Every capability a role can hold, for the role editor. Super-admin only.</summary>
    [HttpGet]
    public IActionResult List() =>
        permissions.Current.IsSuperAdmin
            ? Ok(catalog.All.Select(c => new CapabilityEntry(c.Code, c.LabelKey, c.DescriptionKey)).ToList())
            : ApiResults.Fail(StatusCodes.Status403Forbidden, ErrorCodes.Forbidden, "Admin role required.");
}
