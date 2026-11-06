using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Struo.Api.Auth;
using Struo.Api.Http;
using Struo.Application.Abstractions;
using Struo.Application.Security;
using Struo.Domain.Query;
using ErrorCodes = Struo.Api.Http.ErrorCodes; // disambiguates from the global `HotChocolate.ErrorCodes` using (GraphQl)

namespace Struo.Api.Controllers;

/// <summary>Queries and clears the per-account login lock (<see cref="ILoginAttemptThrottle"/>), addressed
/// by email. Read on the <c>user</c> collection shows lock state and write on it clears a lock. The answer
/// never depends on whether an account with that email exists: the lock is keyed on the address alone,
/// exactly as <c>AuthController.Login</c> counts it, so an unknown address answers in the same shape and
/// status as a known one. The one exception is a super-admin account, whose lock only a super-admin can
/// clear; the query reports that per address as <c>clearable</c>.</summary>
[ApiController]
[Route("api/login-locks")]
[Authorize(AuthenticationSchemes = AuthSchemes.CookieOrBearer)]
public sealed class LoginLocksController(
    ILoginAttemptThrottle throttle, ICurrentPermissions permissions, ICurrentUserAccessor currentUser,
    IUserCredentialStore users, SuperAdminGuard superAdmins, ILogger<LoginLocksController> logger) : ControllerBase
{
    /// <summary>The most addresses one query accepts.</summary>
    public const int MaxQueryEmails = 100;

    public sealed record LoginLockQuery(List<string>? Emails);

    public sealed record LoginLockState(string Email, bool Locked, int RetryAfterSeconds, bool Clearable);

    /// <summary>One entry per requested address, in request order (a repeated address answers once per
    /// occurrence). The throttle is asked once per distinct address and the accounts are looked up in
    /// one query.</summary>
    [HttpPost("query")]
    public async Task<IActionResult> Query([FromBody] LoginLockQuery? body, CancellationToken ct)
    {
        if (!permissions.Current.CanRead(UserCollection.Name))
            return ApiResults.Fail(StatusCodes.Status403Forbidden, ErrorCodes.Forbidden,
                "Read permission on the user collection is required.");
        if (Validate(body?.Emails) is { } invalid) return invalid;

        var requested = body!.Emails!;
        var distinct = requested.Select(EmailAddress.Normalize).Distinct(StringComparer.Ordinal).ToList();
        var protectedEmails = superAdmins.CallerIsSuperAdmin
            ? new HashSet<string>(StringComparer.Ordinal)
            : await superAdmins.SuperAdminEmailsAsync(distinct, ct);

        var states = new Dictionary<string, LoginThrottleState>(StringComparer.Ordinal);
        foreach (var email in distinct)
            states[email] = await throttle.GetStateAsync(email, ct);

        var answer = requested.Select(raw =>
        {
            var email = EmailAddress.Normalize(raw);
            var state = states[email];
            return new LoginLockState(
                raw, state.IsBlocked, state.IsBlocked ? state.RetryAfterSeconds : 0, !protectedEmails.Contains(email));
        }).ToList();
        return Ok(answer);
    }

    [HttpDelete]
    public async Task<IActionResult> Delete([FromQuery] string? email, CancellationToken ct)
    {
        if (!permissions.Current.CanWrite(UserCollection.Name))
            return ApiResults.Fail(StatusCodes.Status403Forbidden, ErrorCodes.Forbidden,
                "Write permission on the user collection is required.");
        if (!EmailAddress.IsUsable(email))
            return ApiResults.Fail(StatusCodes.Status400BadRequest, ErrorCodes.BadUserInput, "An email is required.");

        // Looked up before the lock is cleared, so a failing lookup leaves the lock and the record in step.
        var target = await users.FindByEmailAsync(email!, ct);
        var actor = currentUser.GetCurrentUserId();

        // A super-admin account's lock is cleared only by a super-admin: a caller with write on users
        // could otherwise keep guessing at an administrator's password by clearing the lock between attempts.
        if (target is not null)
        {
            try
            {
                await superAdmins.RequireForUserAsync(target.Id, "clear the lock on a super-admin account", ct);
            }
            catch (PermissionDeniedException refused)
            {
                logger.LogWarning(
                    refused, "Login lock clear refused for {ActorId}: target {TargetUserId} is a super-admin", actor, target.Id);
                return ApiResults.Fail(StatusCodes.Status403Forbidden, ErrorCodes.Forbidden, refused.Message);
            }
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

    private static ObjectResult? Validate(List<string>? emails)
    {
        if (emails is null || emails.Count == 0)
            return ApiResults.Fail(StatusCodes.Status400BadRequest, ErrorCodes.BadUserInput,
                "At least one email is required.");
        if (emails.Count > MaxQueryEmails)
            return ApiResults.Fail(StatusCodes.Status400BadRequest, ErrorCodes.BadUserInput,
                $"At most {MaxQueryEmails} emails can be queried at once.");
        return emails.All(e => EmailAddress.IsUsable(e))
            ? null
            : ApiResults.Fail(StatusCodes.Status400BadRequest, ErrorCodes.BadUserInput,
                "Every email must be non-blank and at most 255 characters.");
    }
}
