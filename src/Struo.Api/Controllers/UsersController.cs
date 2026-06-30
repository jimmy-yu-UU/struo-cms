using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SqlSugar;
using Struo.Api.Auth;
using Struo.Application.Security;
using Struo.Infrastructure.Identity;

namespace Struo.Api.Controllers;

[ApiController]
[Route("api/users")]
[Authorize(AuthenticationSchemes = AuthSchemes.CookieOrBearer)]
public sealed class UsersController(ISqlSugarClient db, IPasswordHasher hasher, IUserCredentialStore store) : ControllerBase
{
    // hasher and store are consumed by Task 9 (create/password actions); kept here to avoid ctor churn.
    private readonly IPasswordHasher _hasher = hasher;
    private readonly IUserCredentialStore _store = store;

    [HttpPost("{id:guid}/access-token")]
    public async Task<IActionResult> GenerateToken(Guid id, CancellationToken ct)
    {
        var (token, hash) = AccessTokenHasher.Generate();
        var updated = await db.Updateable<User>()
            .SetColumns(u => u.AccessToken == hash).Where(u => u.Id == id).ExecuteCommandAsync(ct);
        if (updated == 0) return NotFound();
        return Ok(new { data = new { token } }); // shown once
    }

    [HttpDelete("{id:guid}/access-token")]
    public async Task<IActionResult> RevokeToken(Guid id, CancellationToken ct)
    {
        var updated = await db.Updateable<User>()
            .SetColumns(u => u.AccessToken == null).Where(u => u.Id == id).ExecuteCommandAsync(ct);
        return updated == 0 ? NotFound() : NoContent();
    }
}
