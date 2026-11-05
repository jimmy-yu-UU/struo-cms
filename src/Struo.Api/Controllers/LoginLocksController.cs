using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Struo.Api.Auth;
using Struo.Api.Http;
using Struo.Application.Abstractions;
using Struo.Application.Security;
using ErrorCodes = Struo.Api.Http.ErrorCodes; // disambiguates from the global `HotChocolate.ErrorCodes` using (GraphQl)

namespace Struo.Api.Controllers;

/// <summary>Queries and clears the per-account login lock (<see cref="ILoginAttemptThrottle"/>), addressed
/// by email. Reserved for a super-admin or a caller whose roles hold the <c>login.unlock</c> capability.
/// The answer never depends on whether an account with that email exists: the lock is keyed on the
/// address alone, exactly as <c>AuthController.Login</c> counts it, so an unknown address answers in the
/// same shape and status as a known one. The one exception is clearing the lock on a super-admin
/// account, which only a super-admin may do.</summary>
[ApiController]
[Route("api/login-locks")]
[Authorize(AuthenticationSchemes = AuthSchemes.CookieOrBearer)]
public sealed class LoginLocksController(
    ILoginAttemptThrottle throttle, ICurrentPermissions permissions, ICurrentUserAccessor currentUser,
    IUserCredentialStore users, IRolePermissionStore rolePermissions, ILogger<LoginLocksController> logger) : ControllerBase
{
    public sealed record LoginLockState(bool Locked, int RetryAfterSeconds);

    [HttpGet]
    public async Task<IActionResult> Get([FromQuery] string? email, CancellationToken ct)
    {
        if (Reject(email) is { } rejected) return rejected;

        var state = await throttle.GetStateAsync(email!, ct);
        return Ok(new LoginLockState(state.IsBlocked, state.IsBlocked ? state.RetryAfterSeconds : 0));
    }

    [HttpDelete]
    public async Task<IActionResult> Delete([FromQuery] string? email, CancellationToken ct)
    {
        if (Reject(email) is { } rejected) return rejected;

        // Looked up before the lock is cleared, so a failing lookup leaves the lock and the record in step.
        var target = await users.FindByEmailAsync(email!, ct);
        var actor = currentUser.GetCurrentUserId();

        // A super-admin account's lock is cleared only by a super-admin: a capability holder could
        // otherwise keep guessing at an administrator's password by clearing the lock between attempts.
        if (target is not null && !permissions.Current.IsSuperAdmin
            && PermissionResolver.Resolve(await rolePermissions.LoadForUserAsync(target.Id, ct)).IsSuperAdmin)
        {
            logger.LogWarning(
                "Login lock clear refused for {ActorId}: target {TargetUserId} is a super-admin", actor, target.Id);
            return ApiResults.Fail(StatusCodes.Status403Forbidden, ErrorCodes.Forbidden,
                "Only a super-admin can clear the lock on a super-admin account.");
        }

        await throttle.ResetAsync(email!, ct);

        // The project has no audit table; this line is the record. It names the actor and the account by
        // id and never carries the email.
        if (!logger.IsEnabled(LogLevel.Information)) return NoContent();
        if (target is null)
            logger.LogInformation(
                "Login lock cleared by {ActorId} for an account that does not exist", actor);
        else
            logger.LogInformation("Login lock cleared by {ActorId} for user {TargetUserId}", actor, target.Id);
        return NoContent();
    }

    private ObjectResult? Reject(string? email)
    {
        if (!permissions.Current.HasCapability(SystemCapabilities.LoginUnlock))
            return ApiResults.Fail(StatusCodes.Status403Forbidden, ErrorCodes.Forbidden,
                "The login.unlock permission is required.");
        return EmailAddress.IsUsable(email)
            ? null
            : ApiResults.Fail(StatusCodes.Status400BadRequest, ErrorCodes.BadUserInput, "An email is required.");
    }
}
