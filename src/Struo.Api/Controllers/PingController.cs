// src/Struo.Api/Controllers/PingController.cs
using Microsoft.AspNetCore.Mvc;

namespace Struo.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public sealed class PingController : ControllerBase
{
    [HttpGet]
    public IActionResult Get() => Ok(new
    {
        status = "ok",
        service = "StruoCMS",
        utc = DateTime.UtcNow
    });
}
