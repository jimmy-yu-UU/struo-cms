using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.Extensions.Caching.Distributed;

namespace Struo.Api.Auth;

/// <summary>Server-side session store for cookie auth, backed by IDistributedCache (Redis in
/// real environments, in-memory in tests). Enables immediate revocation (logout / logout-all).</summary>
public sealed class DistributedCacheTicketStore(IDistributedCache cache) : ITicketStore
{
    private const string Prefix = "auth-ticket:";
    private static readonly DistributedCacheEntryOptions Expiry = new()
    {
        SlidingExpiration = TimeSpan.FromHours(8)
    };

    public async Task<string> StoreAsync(AuthenticationTicket ticket)
    {
        var key = Prefix + Guid.NewGuid().ToString("N");
        await RenewAsync(key, ticket);
        return key;
    }

    public Task RenewAsync(string key, AuthenticationTicket ticket)
    {
        var bytes = TicketSerializer.Default.Serialize(ticket);
        return cache.SetAsync(key, bytes, Expiry);
    }

    public async Task<AuthenticationTicket?> RetrieveAsync(string key)
    {
        var bytes = await cache.GetAsync(key);
        return bytes is null ? null : TicketSerializer.Default.Deserialize(bytes);
    }

    public Task RemoveAsync(string key) => cache.RemoveAsync(key);
}
