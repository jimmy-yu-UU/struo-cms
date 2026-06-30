using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SqlSugar;
using Struo.Api.Auth;
using Struo.Application.Security;
using Struo.Infrastructure.Identity;

namespace Struo.Api.Controllers;

public sealed record CreateUserRequest(string Email, string Password, string? Name);
public sealed record ChangePasswordRequest(string NewPassword, string? CurrentPassword);

[ApiController]
[Route("api/users")]
[Authorize(AuthenticationSchemes = AuthSchemes.CookieOrBearer)]
public sealed class UsersController(ISqlSugarClient db, IPasswordHasher hasher, IUserCredentialStore store) : ControllerBase
{
    // hasher and store are consumed by Task 9 (create/password actions); kept here to avoid ctor churn.
    private readonly IPasswordHasher _hasher = hasher;
    private readonly IUserCredentialStore _store = store;

    private const int MinPasswordLength = 8;

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateUserRequest body, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(body.Email))
            return BadRequest(new { error = new { message = "Email is required." } });
        if (body.Password is null || body.Password.Length < MinPasswordLength)
            return BadRequest(new { error = new { message = $"Password must be at least {MinPasswordLength} characters." } });
        if (await _store.FindByEmailAsync(body.Email, ct) is not null)
            return Conflict(new { error = new { message = "Email already in use." } });

        var id = Guid.CreateVersion7();
        await db.Insertable(new User
        {
            Id = id, Email = body.Email, Password = _hasher.Hash(body.Password), Name = body.Name, IsActive = true
        }).ExecuteCommandAsync(ct);
        return Created($"/api/items/user/{id}", new { data = new { id, email = body.Email, name = body.Name } });
    }

    [HttpPut("{id:guid}/password")]
    public async Task<IActionResult> ChangePassword(Guid id, [FromBody] ChangePasswordRequest body, CancellationToken ct)
    {
        if (body.NewPassword is null || body.NewPassword.Length < MinPasswordLength)
            return BadRequest(new { error = new { message = $"Password must be at least {MinPasswordLength} characters." } });

        var updated = await db.Updateable<User>()
            .SetColumns(u => u.Password == _hasher.Hash(body.NewPassword))
            .Where(u => u.Id == id).ExecuteCommandAsync(ct);
        return updated == 0 ? NotFound() : NoContent();
    }

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
