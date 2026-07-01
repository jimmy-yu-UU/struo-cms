using SqlSugar;
using Struo.Application.Security;

namespace Struo.Infrastructure.Identity;

/// <summary>External-login user lookup/creation. Email match is case-insensitive; created users are
/// active, role-less, and have an empty password (never password-loginable).</summary>
public sealed class SqlSugarExternalUserStore(ISqlSugarClient db) : IExternalUserStore
{
    public async Task<ExternalUserMatch?> FindByEmailAsync(string email, CancellationToken ct = default)
    {
        var lowered = email.ToLowerInvariant();
        var u = await db.Queryable<User>()
            .Where(x => x.Email.ToLower() == lowered)
            .FirstAsync(ct);
        return u is null ? null : new ExternalUserMatch(u.Id, u.IsActive);
    }

    public async Task<Guid> CreateExternalUserAsync(string email, string? name, CancellationToken ct = default)
    {
        var id = Guid.CreateVersion7();
        await db.Insertable(new User
        {
            Id = id, Email = email, Name = name, Password = "", IsActive = true, AccessToken = null
        }).ExecuteCommandAsync(ct);
        return id;
    }
}
