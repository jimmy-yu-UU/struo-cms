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
/// same shape and status as a known one.</summary>
[ApiController]
[Route("api/login-locks")]
[Authorize(AuthenticationSchemes = AuthSchemes.CookieOrBearer)]
public sealed class LoginLocksController(
    ILoginAttemptThrottle throttle, ICurrentPermissions permissions, ICurrentUserAccessor currentUser,
    IUserCredentialStore users, ILogger<LoginLocksController> logger) : ControllerBase
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

        await throttle.ResetAsync(email!, ct);

        // The project has no audit table; this line is the record. It names the actor and the account by
        // id and never carries the email.
        var actor = currentUser.GetCurrentUserId();
        var target = await users.FindByEmailAsync(EmailAddress.Normalize(email!), ct);
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
        return EmailAddress.IsWellFormed(email)
            ? null
            : ApiResults.Fail(StatusCodes.Status400BadRequest, ErrorCodes.BadUserInput, "A valid email is required.");
    }
}
