using SqlSugar;
using Struo.Application.Security;
using Struo.Infrastructure.Identity;
using Struo.Infrastructure.Migrations;

namespace Struo.Api.Auth;

/// <summary>
/// Production startup warning for an active user whose email is the default bootstrap address
/// (<see cref="CoreSeedData.DefaultAdminEmail"/>). Run after the migration gate, so the users table exists.
/// An advisory check: a failing read logs a warning and never fails startup.
/// </summary>
public static class DefaultAdminAccountWarnings
{
    public static async Task LogAsync(ISqlSugarClient db, IHostEnvironment env, ILogger logger)
    {
        if (!env.IsProduction()) return;
        var email = EmailAddress.Normalize(CoreSeedData.DefaultAdminEmail);
        try
        {
            if (!await db.Queryable<User>().AnyAsync(u => SqlFunc.ToLower(u.Email) == email && u.IsActive)) return;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(
                ex,
                "Could not check for an active user with the default bootstrap email ({ExceptionType}).",
                ex.GetType().Name);
            return;
        }

        logger.LogWarning(
            "A user with the default bootstrap email {Email} can still sign in, and anyone who knows that address " +
            "can aim password guesses at it. Change its email in the admin UI, or create another administrator " +
            "and deactivate this account.",
            CoreSeedData.DefaultAdminEmail);
    }
}
