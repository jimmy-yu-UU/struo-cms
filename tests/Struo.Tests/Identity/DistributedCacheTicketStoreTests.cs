using AwesomeAssertions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SqlSugar;
using System.Security.Claims;
using Struo.Api.Auth;
using Struo.Application.Security;
using Struo.Infrastructure.Identity;
using Struo.Tests.Support;
using Xunit;

namespace Struo.Tests.Identity;

public class DistributedCacheTicketStoreTests
{
    private static AuthenticationTicket Ticket(Guid? userId = null)
    {
        var identity = new ClaimsIdentity("Cookies");
        if (userId is { } id)
            identity.AddClaim(new Claim(ClaimTypes.NameIdentifier, id.ToString()));
        return new AuthenticationTicket(new ClaimsPrincipal(identity), "Cookies");
    }

    private static IDistributedCache MemoryCache() =>
        new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions()));

    /// <summary>
    /// A real ServiceProvider whose scopes each resolve a fresh (but shared-SQLite-backed)
    /// IUserSessionStore, mirroring how the real app resolves it via IServiceScopeFactory from
    /// DistributedCacheTicketStore (a singleton) per store/renew/remove call. Also owns the
    /// IDistributedCache and ILogger so a test can seed the cache directly (simulating a session that
    /// predates the index) and inspect what got logged.
    /// </summary>
    private sealed class Harness : IDisposable
    {
        public readonly SqliteTestDatabase Db = new();
        public readonly SqlSugarClient Client;
        public readonly IServiceScopeFactory ScopeFactory;
        public readonly IDistributedCache Cache = MemoryCache();
        public readonly ListLogger<DistributedCacheTicketStore> Logger = new();
        private readonly ServiceProvider _provider;

        public Harness()
        {
            Client = new SqlSugarClient(new ConnectionConfig
            {
                ConnectionString = Db.ConnectionString,
                DbType = DbType.Sqlite,
                IsAutoCloseConnection = true,
            });
            Client.CodeFirst.InitTables<UserSession>();

            var services = new ServiceCollection();
            services.AddScoped<ISqlSugarClient>(_ => Client);
            services.AddScoped<IUserSessionStore, SqlSugarUserSessionStore>();
            _provider = services.BuildServiceProvider();
            ScopeFactory = _provider.GetRequiredService<IServiceScopeFactory>();
        }

        public DistributedCacheTicketStore NewStore() => new(Cache, ScopeFactory, Logger);

        public Task<IReadOnlyList<string>> KeysFor(Guid userId) =>
            new SqlSugarUserSessionStore(Client).ListTicketKeysForUserAsync(userId);

        public void Dispose()
        {
            _provider.Dispose();
            Db.Dispose();
        }
    }

    /// <summary>Stands in for an unreachable session-index dependency (DB down, etc.).</summary>
    private sealed class ThrowingUserSessionStore : IUserSessionStore
    {
        private static InvalidOperationException Failure() => new("session index unreachable");
        public Task RecordAsync(Guid userId, string ticketKey, DateTime createdAtUtc, DateTime expiresAtUtc, CancellationToken ct = default) =>
            throw Failure();
        public Task<bool> RenewAsync(string ticketKey, DateTime expiresAtUtc, CancellationToken ct = default) =>
            throw Failure();
        public Task RemoveByTicketKeyAsync(string ticketKey, CancellationToken ct = default) => throw Failure();
        public Task RemoveExpiredForUserAsync(Guid userId, DateTime nowUtc, CancellationToken ct = default) => throw Failure();
        public Task<IReadOnlyList<string>> ListTicketKeysForUserAsync(Guid userId, CancellationToken ct = default) => throw Failure();
        public Task RemoveAllForUserAsync(Guid userId, CancellationToken ct = default) => throw Failure();
    }

    [Fact]
    public async Task Store_then_retrieve_round_trips_then_remove_clears()
    {
        using var h = new Harness();
        var store = h.NewStore();
        var key = await store.StoreAsync(Ticket(Guid.CreateVersion7()));

        var retrieved = await store.RetrieveAsync(key);
        retrieved.Should().NotBeNull();
        retrieved!.Principal.FindFirst(ClaimTypes.NameIdentifier).Should().NotBeNull();

        await store.RemoveAsync(key);
        (await store.RetrieveAsync(key)).Should().BeNull();
    }

    [Fact]
    public async Task StoreAsync_indexes_the_new_session_under_the_ticket_users_id()
    {
        using var h = new Harness();
        var store = h.NewStore();
        var userId = Guid.CreateVersion7();

        var key = await store.StoreAsync(Ticket(userId));

        (await h.KeysFor(userId)).Should().ContainSingle().Which.Should().Be(key);
    }

    [Fact]
    public async Task RemoveAsync_deletes_the_index_row_for_that_key()
    {
        using var h = new Harness();
        var store = h.NewStore();
        var userId = Guid.CreateVersion7();
        var key = await store.StoreAsync(Ticket(userId));

        await store.RemoveAsync(key);

        (await h.KeysFor(userId)).Should().BeEmpty();
    }

    [Fact]
    public async Task RenewAsync_extends_the_indexed_rows_ExpiresAt()
    {
        using var h = new Harness();
        var store = h.NewStore();
        var userId = Guid.CreateVersion7();
        var key = await store.StoreAsync(Ticket(userId));

        var before = await h.Client.Queryable<UserSession>().Where(s => s.TicketKey == key).FirstAsync();

        // Renewing should push ExpiresAt further into the future than the original StoreAsync did.
        await Task.Delay(10);
        await store.RenewAsync(key, Ticket(userId));

        var after = await h.Client.Queryable<UserSession>().Where(s => s.TicketKey == key).FirstAsync();
        after.Should().NotBeNull();
        after!.ExpiresAt.Should().BeAfter(before!.ExpiresAt);
    }

    /// <summary>
    /// A failure to identify the caller must never break login: StoreAsync still stores the ticket
    /// itself (cache-side), it just skips writing an index row it couldn't attribute to anyone.
    /// </summary>
    [Fact]
    public async Task StoreAsync_with_no_NameIdentifier_claim_still_stores_the_ticket_but_writes_no_index_row()
    {
        using var h = new Harness();
        var store = h.NewStore();

        var key = await store.StoreAsync(Ticket(userId: null));

        (await store.RetrieveAsync(key)).Should().NotBeNull();
        var rowCount = await h.Client.Queryable<UserSession>().CountAsync();
        rowCount.Should().Be(0);
    }

    [Fact]
    public async Task StoreAsync_sweeps_the_same_users_expired_rows_but_not_another_users()
    {
        using var h = new Harness();
        var store = h.NewStore();
        var userA = Guid.CreateVersion7();
        var userB = Guid.CreateVersion7();

        // Seed a stale (already-expired) row for userA directly, plus a live row for userB.
        var sessionStore = new SqlSugarUserSessionStore(h.Client);
        var now = DateTime.UtcNow;
        await sessionStore.RecordAsync(userA, "stale-a-key", now.AddHours(-9), now.AddHours(-1));
        await sessionStore.RecordAsync(userB, "live-b-key", now, now.AddHours(8));

        // A fresh login for userA must sweep ONLY userA's expired row.
        await store.StoreAsync(Ticket(userA));

        (await h.KeysFor(userA)).Should().NotContain("stale-a-key");
        (await h.KeysFor(userB)).Should().ContainSingle().Which.Should().Be("live-b-key");
    }

    /// <summary>
    /// A cookie session that existed before this feature shipped (or whose StoreAsync could not
    /// attribute a user id at the time) has no user_sessions row. Without a back-fill, such a session
    /// would sit in the cache indefinitely (8h sliding expiration, self-renewing forever) yet never be
    /// findable by IUserSessionRevocationService — the exact "can't be revoked" hole this feature
    /// exists to close. RenewAsync must back-fill a row the first time it observes such a ticket.
    /// </summary>
    [Fact]
    public async Task RenewAsync_backfills_an_index_row_for_a_session_that_predates_the_index()
    {
        using var h = new Harness();
        var store = h.NewStore();
        var userId = Guid.CreateVersion7();
        var ticket = Ticket(userId);
        const string key = "auth-ticket:pre-existing-session";

        // Seed the cache directly (bypassing StoreAsync) to model a ticket that was already alive
        // before the user_sessions index existed.
        await h.Cache.SetAsync(key, TicketSerializer.Default.Serialize(ticket),
            new DistributedCacheEntryOptions { SlidingExpiration = AuthSchemes.SessionLifetime });
        (await h.KeysFor(userId)).Should().BeEmpty("the ticket was seeded directly, with no index row");

        await store.RenewAsync(key, ticket);

        (await h.KeysFor(userId)).Should().Contain(key, "the renewal must have back-filled the missing row");
    }

    /// <summary>
    /// TOCTOU: a renewal that was already in flight (e.g. queued on Response.OnStarting) when a
    /// concurrent revocation removed this exact ticket must not write it back to life.
    /// </summary>
    [Fact]
    public async Task RenewAsync_does_not_resurrect_an_already_removed_ticket()
    {
        using var h = new Harness();
        var store = h.NewStore();
        var userId = Guid.CreateVersion7();
        var ticket = Ticket(userId);
        var key = await store.StoreAsync(ticket);

        // Simulate the revocation racing ahead of this renewal.
        await store.RemoveAsync(key);

        await store.RenewAsync(key, ticket);

        (await store.RetrieveAsync(key)).Should().BeNull("a renewal must not resurrect an already-revoked ticket");
        (await h.KeysFor(userId)).Should().BeEmpty("nor should it re-create the index row");
    }

    /// <summary>
    /// RenewAsync runs from CookieAuthenticationHandler's Response.OnStarting callback. A transient
    /// failure writing the (convenience) index must not turn an otherwise-successful response into a
    /// 500 right as headers are about to be written — the cache-side renewal is what actually keeps the
    /// caller signed in, and must still succeed.
    /// </summary>
    [Fact]
    public async Task RenewAsync_does_not_throw_when_the_index_write_fails_and_logs_it()
    {
        var services = new ServiceCollection();
        services.AddScoped<IUserSessionStore, ThrowingUserSessionStore>();
        using var provider = services.BuildServiceProvider();
        var logger = new ListLogger<DistributedCacheTicketStore>();
        var cache = MemoryCache();
        var store = new DistributedCacheTicketStore(cache, provider.GetRequiredService<IServiceScopeFactory>(), logger);
        var ticket = Ticket(Guid.CreateVersion7());
        const string key = "auth-ticket:renew-index-failure";
        await cache.SetAsync(key, TicketSerializer.Default.Serialize(ticket),
            new DistributedCacheEntryOptions { SlidingExpiration = AuthSchemes.SessionLifetime });

        var act = () => store.RenewAsync(key, ticket);

        await act.Should().NotThrowAsync(
            "an index-write failure during a sliding-expiration renewal must not fail the response");
        logger.Entries.Should().Contain(e => e.Level == LogLevel.Error);
        (await store.RetrieveAsync(key)).Should().NotBeNull("the cache-side renewal must still have succeeded");
    }
}
