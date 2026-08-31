namespace Struo.Application.Security;

/// <summary>
/// Kills every live cookie session for a user: removes each ticket from the distributed cache (the
/// actual session payload) and then clears the <c>user_sessions</c> index. Called after a password
/// change succeeds — both the self-service and admin-reset branches run through the same
/// <c>UsersController.ChangePassword</c> action — so a stolen or shared session dies immediately
/// instead of continuing to work under the old credential. Deliberately does not touch bearer access
/// tokens; those have their own separate revocation path
/// (<c>DELETE /api/users/{id}/access-token</c>).
/// </summary>
public interface IUserSessionRevocationService
{
    Task RevokeAllForUserAsync(Guid userId, CancellationToken ct = default);
}
