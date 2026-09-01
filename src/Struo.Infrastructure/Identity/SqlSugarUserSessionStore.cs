using SqlSugar;
using Struo.Application.Security;

namespace Struo.Infrastructure.Identity;

public sealed class SqlSugarUserSessionStore(ISqlSugarClient db) : IUserSessionStore
{
    public Task RecordAsync(
        Guid userId, string ticketKey, DateTime createdAtUtc, DateTime expiresAtUtc, CancellationToken ct = default) =>
        db.Insertable(new UserSession
        {
            Id = Guid.CreateVersion7(), UserId = userId, TicketKey = ticketKey,
            CreatedAt = createdAtUtc, ExpiresAt = expiresAtUtc,
        }).ExecuteCommandAsync(ct);

    public async Task<bool> RenewAsync(string ticketKey, DateTime expiresAtUtc, CancellationToken ct = default) =>
        await db.Updateable<UserSession>()
            .SetColumns(s => new UserSession { ExpiresAt = expiresAtUtc })
            .Where(s => s.TicketKey == ticketKey)
            .ExecuteCommandAsync(ct) > 0;

    public Task RemoveByTicketKeyAsync(string ticketKey, CancellationToken ct = default) =>
        db.Deleteable<UserSession>().Where(s => s.TicketKey == ticketKey).ExecuteCommandAsync(ct);

    public Task RemoveExpiredForUserAsync(Guid userId, DateTime nowUtc, CancellationToken ct = default) =>
        db.Deleteable<UserSession>()
            .Where(s => s.UserId == userId && s.ExpiresAt < nowUtc)
            .ExecuteCommandAsync(ct);

    public async Task<IReadOnlyList<string>> ListTicketKeysForUserAsync(Guid userId, CancellationToken ct = default) =>
        await db.Queryable<UserSession>()
            .Where(s => s.UserId == userId)
            .Select(s => s.TicketKey)
            .ToListAsync(ct);

    public Task RemoveAllForUserAsync(Guid userId, CancellationToken ct = default) =>
        db.Deleteable<UserSession>().Where(s => s.UserId == userId).ExecuteCommandAsync(ct);
}
