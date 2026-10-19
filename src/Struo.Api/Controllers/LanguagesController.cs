using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Struo.Api.Auth;
using Struo.Application.Localization;

namespace Struo.Api.Controllers;

[ApiController]
[Route("api/languages")]
[Authorize(AuthenticationSchemes = AuthSchemes.CookieOrBearer)]
public sealed class LanguagesController(ILanguageProvider languages) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        var data = (await languages.GetEnabledAsync())
            .Select(l => new { code = l.Code, name = l.Name, isDefault = l.IsDefault })
            .ToList();
        return Ok(data);
    }
}
