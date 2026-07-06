using SqlSugar;
using Struo.Application.Security;

namespace Struo.Infrastructure.Identity;

public sealed class SqlSugarUserCredentialStore(ISqlSugarClient db) : IUserCredentialStore
{
    public async Task<UserCredential?> FindByEmailAsync(string email, CancellationToken ct = default)
    {
        var lowered = email.ToLowerInvariant();
        var u = await db.Queryable<User>()
            .Where(x => x.Email.ToLower() == lowered)
            .FirstAsync(ct);
        return u is null ? null : new UserCredential(u.Id, u.Password, u.IsActive);
    }

    public async Task<UserCredential?> FindByAccessTokenAsync(string tokenHash, CancellationToken ct = default)
    {
        var u = await db.Queryable<User>()
            .Where(x => x.AccessToken == tokenHash)
            .FirstAsync(ct);
        return u is null ? null : new UserCredential(u.Id, u.Password, u.IsActive, u.AccessTokenLastUsedAt);
    }

    public Task TouchAccessTokenLastUsedAsync(Guid userId, DateTime nowUtc, CancellationToken ct = default) =>
        db.Updateable<User>()
            .SetColumns(u => u.AccessTokenLastUsedAt == nowUtc)
            .Where(u => u.Id == userId)
            .ExecuteCommandAsync(ct);
}
