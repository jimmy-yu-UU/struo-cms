using SqlSugar;
using Struo.Application.Security;

namespace Struo.Infrastructure.Identity;

/// <summary>External-login user lookup/creation. The email match is the database collation's, so the returned
/// <see cref="ExternalUserMatch.Email"/> is the stored spelling for the caller to compare; created users are
/// active, role-less, and have an empty password (never password-loginable).</summary>
public sealed class SqlSugarExternalUserStore(ISqlSugarClient db) : IExternalUserStore
{
    public async Task<ExternalUserMatch?> FindByEmailAsync(string email, CancellationToken ct = default)
    {
        var lowered = EmailAddress.Normalize(email);
        var u = await db.Queryable<User>()
            .Where(x => SqlFunc.ToLower(x.Email) == lowered)
            .FirstAsync(ct);
        return u is null ? null : new ExternalUserMatch(u.Id, u.IsActive, u.Email);
    }

    public async Task<Guid> CreateExternalUserAsync(string email, string? name, CancellationToken ct = default)
    {
        var id = Guid.CreateVersion7();
        await db.Insertable(new User
        {
            Id = id, Email = EmailAddress.Normalize(email), Name = name, Password = "", IsActive = true, AccessToken = null
        }).ExecuteCommandAsync(ct);
        return id;
    }
}
