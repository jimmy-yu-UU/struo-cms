using SqlSugar;
using Struo.Application.Security;

namespace Struo.Infrastructure.Identity;

public sealed class SqlSugarUserSessionStore(ISqlSugarClient db) : IUserSessionStore
{
    // Keeps one DELETE under the smallest bound-parameter limit of the supported backends.
    private const int DeleteChunkSize = 500;

    public Task RecordAsync(
        Guid userId, string ticketKey, DateTime createdAtUtc, DateTime expiresAtUtc, CancellationToken ct = default) =>
        db.Insertable(new UserSession
        {
            Id = Guid.CreateVersion7(), UserId = userId, TicketKey = ticketKey,
            CreatedAt = createdAtUtc, ExpiresAt = expiresAtUtc,
        }).ExecuteCommandAsync(ct);

    public async Task<bool> RenewAsync(string ticketKey, Guid userId, DateTime expiresAtUtc, CancellationToken ct = default) =>
        await db.Updateable<UserSession>()
            .SetColumns(s => new UserSession { UserId = userId, ExpiresAt = expiresAtUtc })
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

    public async Task RemoveByTicketKeysAsync(IReadOnlyCollection<string> ticketKeys, CancellationToken ct = default)
    {
        foreach (var chunk in ticketKeys.Chunk(DeleteChunkSize))
            await db.Deleteable<UserSession>().Where(s => chunk.Contains(s.TicketKey)).ExecuteCommandAsync(ct);
    }
}
