using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Struo.Application.Security;

namespace Struo.Api.Auth;

/// <summary>Server-side session store for cookie auth, backed by IDistributedCache (Redis in
/// real environments, in-memory in tests). Enables immediate revocation (logout / logout-all).
/// <para>
/// Also keeps the <c>UserSession</c> index (<see cref="IUserSessionStore"/>) in sync, so a user's
/// live sessions can be enumerated and revoked (see <c>IUserSessionRevocationService</c>) even though
/// <see cref="IDistributedCache"/> itself has no key-scan/set operation. This class is registered
/// <c>AddSingleton</c> — required because <c>CookieAuthenticationOptions.SessionStore</c> is resolved
/// through the options system's singleton <c>IOptionsMonitor</c>/<c>IOptionsFactory</c> chain (measured:
/// registering it <c>AddScoped</c> instead throws
/// <c>InvalidOperationException: Cannot resolve scoped service 'DistributedCacheTicketStore' from root
/// provider</c> the moment a cookie is authenticated) — so index writes, which need a scoped
/// <see cref="IUserSessionStore"/>/<c>ISqlSugarClient</c>, go through a fresh
/// <see cref="IServiceScopeFactory"/> scope per call rather than a constructor-injected dependency.
/// </para>
/// </summary>
public sealed class DistributedCacheTicketStore(
    IDistributedCache cache, IServiceScopeFactory scopeFactory, ILogger<DistributedCacheTicketStore> logger)
    : ITicketStore
{
    private const string Prefix = "auth-ticket:";
    private static readonly DistributedCacheEntryOptions Expiry =
        new() { SlidingExpiration = AuthSchemes.SessionLifetime };

    public async Task<string> StoreAsync(AuthenticationTicket ticket)
    {
        var key = Prefix + Guid.NewGuid().ToString("N");
        await PersistAsync(key, ticket);
        await IndexNewSessionAsync(key, ticket);
        return key;
    }

    public async Task RenewAsync(string key, AuthenticationTicket ticket)
    {
        // TOCTOU guard: CookieAuthenticationHandler queues a sliding-expiration renewal on
        // Response.OnStarting using the ticket it read at the START of the request. If a concurrent
        // revocation (RevokeAllForUserAsync, or the per-request liveness check's SignOutAsync) removes
        // this exact ticket in the meantime, blindly re-persisting it here would write it back to life
        // — alive again in the cache, and (per the back-fill below) freshly re-indexed. Checking the
        // cache entry still exists right before persisting closes that window; a revocation landing in
        // the instant between this check and the SetAsync below is not covered (IDistributedCache has
        // no compare-and-swap), but that residual race already existed for the cache write itself.
        if (await cache.GetAsync(key) is null) return;
        await PersistAsync(key, ticket);
        await RenewIndexAsync(key, ticket);
    }

    public async Task<AuthenticationTicket?> RetrieveAsync(string key)
    {
        var bytes = await cache.GetAsync(key);
        return bytes is null ? null : TicketSerializer.Default.Deserialize(bytes);
    }

    public async Task RemoveAsync(string key)
    {
        await cache.RemoveAsync(key);
        await RemoveIndexAsync(key);
    }

    private Task PersistAsync(string key, AuthenticationTicket ticket)
    {
        var bytes = TicketSerializer.Default.Serialize(ticket);
        return cache.SetAsync(key, bytes, Expiry);
    }

    // A ticket whose principal carries no readable user id is stored (cache-side) but left unindexed,
    // rather than throwing and breaking login — StoreAsync runs synchronously as part of the login
    // request, so an uncaught exception here would fail the whole login. Once a userId IS known,
    // StoreAsync's/RemoveAsync's index writes are ordinary awaited calls: a real DB error there
    // propagates like any other write in this codebase. RenewAsync is the one exception (see its own
    // try/catch below) because it runs from Response.OnStarting, where a thrown exception lands at the
    // worst possible point in the response lifecycle rather than failing an ordinary request.

    private async Task IndexNewSessionAsync(string key, AuthenticationTicket ticket)
    {
        if (ExtractUserId(ticket) is not { } userId) return;
        using var scope = scopeFactory.CreateScope();
        var sessions = scope.ServiceProvider.GetRequiredService<IUserSessionStore>();
        var now = DateTime.UtcNow;
        await sessions.RecordAsync(userId, key, now, now + AuthSchemes.SessionLifetime);
        // Opportunistic cleanup: sweep only THIS user's own expired rows, on THIS user's own login —
        // never a full-table scan. Safe only because AuthSchemes.SessionLifetime is the one value both
        // the cookie's own ExpireTimeSpan (AuthWiring) and this index agree on — see its doc comment.
        await sessions.RemoveExpiredForUserAsync(userId, now);
    }

    private async Task RenewIndexAsync(string key, AuthenticationTicket ticket)
    {
        try
        {
            // A ticket with no readable user id leaves the row untouched — same as StoreAsync, there is
            // nobody to attribute it to.
            if (ExtractUserId(ticket) is not { } userId) return;
            using var scope = scopeFactory.CreateScope();
            var sessions = scope.ServiceProvider.GetRequiredService<IUserSessionStore>();
            var expiresAt = DateTime.UtcNow + AuthSchemes.SessionLifetime;
            // Re-attributes the row to this ticket's current user, so a renewal that carries a different
            // principal than the row was last recorded under (e.g. logging in as B over a still-valid
            // cookie for A) moves the row to B rather than leaving it attributed to A.
            if (await sessions.RenewAsync(key, userId, expiresAt)) return;

            // No row matched: this ticket predates the index (or its StoreAsync couldn't attribute a
            // user id at the time it ran). Back-fill one now so the session becomes revocable going
            // forward — otherwise a sliding-expiration session with no index row can never be found by
            // IUserSessionRevocationService and would stay alive, self-renewing, indefinitely.
            await sessions.RecordAsync(userId, key, DateTime.UtcNow, expiresAt);
        }
        catch (Exception ex)
        {
            // Unlike IndexNewSessionAsync/RemoveIndexAsync, this runs from
            // CookieAuthenticationHandler's Response.OnStarting callback — not an ordinary request. A
            // transient failure here must not turn an otherwise-successful response into a 500 right as
            // headers are about to be written; the cache-side renewal (PersistAsync, already awaited
            // above) is what actually keeps the caller signed in, and the index is a revocation
            // convenience structure on top of it.
            logger.LogError(ex, "Failed to renew the session index row for ticket key {TicketKey}.", key);
        }
    }

    private async Task RemoveIndexAsync(string key)
    {
        using var scope = scopeFactory.CreateScope();
        var sessions = scope.ServiceProvider.GetRequiredService<IUserSessionStore>();
        await sessions.RemoveByTicketKeyAsync(key);
    }

    private static Guid? ExtractUserId(AuthenticationTicket ticket) =>
        Guid.TryParse(ticket.Principal.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var id) ? id : null;
}
