using Microsoft.Extensions.Caching.Distributed;
using Struo.Application.Security;

namespace Struo.Infrastructure.Identity;

public sealed class UserSessionRevocationService(IUserSessionStore sessions, IDistributedCache cache)
    : IUserSessionRevocationService
{
    public async Task RevokeAllForUserAsync(Guid userId, CancellationToken ct = default)
    {
        var keys = await sessions.ListTicketKeysForUserAsync(userId, ct);
        foreach (var key in keys)
            await cache.RemoveAsync(key, ct);
        await sessions.RemoveAllForUserAsync(userId, ct);
    }
}
