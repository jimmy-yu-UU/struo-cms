using AwesomeAssertions;
using SqlSugar;
using Struo.Application.Security;
using Struo.Infrastructure.Identity;
using Struo.Tests.Support;
using Xunit;

namespace Struo.Tests.Identity;

/// <summary>
/// <see cref="SqlSugarUserSessionStore"/> is the CRUD seam over <c>user_sessions</c> — the index table
/// that lets a user's live cookie sessions be enumerated and revoked even though the actual ticket
/// payload lives in <c>IDistributedCache</c> (which has no key-scan/set operation of its own). These
/// tests exercise the store directly against a real SQLite table, independent of the ticket store or
/// the HTTP surface that consume it.
/// </summary>
public class SqlSugarUserSessionStoreTests
{
    private static SqlSugarClient NewDb(SqliteTestDatabase db)
    {
        var client = new SqlSugarClient(new ConnectionConfig
        {
            ConnectionString = db.ConnectionString,
            DbType = DbType.Sqlite,
            IsAutoCloseConnection = true,
        });
        client.CodeFirst.InitTables<UserSession>();
        return client;
    }

    [Fact]
    public async Task RecordAsync_then_ListTicketKeysForUserAsync_round_trips()
    {
        using var db = new SqliteTestDatabase();
        var client = NewDb(db);
        var store = new SqlSugarUserSessionStore(client);
        var userId = Guid.CreateVersion7();
        var now = DateTime.UtcNow;

        await store.RecordAsync(userId, "key-1", now, now.AddHours(8));

        var keys = await store.ListTicketKeysForUserAsync(userId);
        keys.Should().ContainSingle().Which.Should().Be("key-1");
    }

    [Fact]
    public async Task ListTicketKeysForUserAsync_does_not_return_another_users_keys()
    {
        using var db = new SqliteTestDatabase();
        var client = NewDb(db);
        var store = new SqlSugarUserSessionStore(client);
        var userA = Guid.CreateVersion7();
        var userB = Guid.CreateVersion7();
        var now = DateTime.UtcNow;

        await store.RecordAsync(userA, "a-key", now, now.AddHours(8));
        await store.RecordAsync(userB, "b-key", now, now.AddHours(8));

        (await store.ListTicketKeysForUserAsync(userA)).Should().BeEquivalentTo(["a-key"]);
        (await store.ListTicketKeysForUserAsync(userB)).Should().BeEquivalentTo(["b-key"]);
    }

    [Fact]
    public async Task RenewAsync_updates_ExpiresAt_for_the_matching_row_and_reports_true()
    {
        using var db = new SqliteTestDatabase();
        var client = NewDb(db);
        var store = new SqlSugarUserSessionStore(client);
        var userId = Guid.CreateVersion7();
        var now = DateTime.UtcNow;
        await store.RecordAsync(userId, "key-1", now, now.AddHours(8));

        var renewedTo = now.AddHours(16);
        var matched = await store.RenewAsync("key-1", userId, renewedTo);

        matched.Should().BeTrue();
        var row = await client.Queryable<UserSession>().Where(s => s.TicketKey == "key-1").FirstAsync();
        row.Should().NotBeNull();
        row!.ExpiresAt.Should().BeCloseTo(renewedTo, TimeSpan.FromSeconds(1));
    }

    [Fact]
    public async Task RenewAsync_reports_false_when_no_row_matches_the_ticket_key()
    {
        using var db = new SqliteTestDatabase();
        var client = NewDb(db);
        var store = new SqlSugarUserSessionStore(client);

        var matched = await store.RenewAsync("no-such-key", Guid.CreateVersion7(), DateTime.UtcNow.AddHours(8));

        matched.Should().BeFalse();
    }

    /// <summary>
    /// Reproduces the stale-attribution defect: a request that still carries a valid session cookie for
    /// user A performs a login as user B, and ASP.NET Core's CookieAuthenticationHandler renews the SAME
    /// ticket key in place with B's principal (RenewAsync, not a new StoreAsync). The index row for that
    /// key must move to B — otherwise B's password change never revokes it, and A's password change wrongly
    /// revokes a session A no longer uses.
    /// </summary>
    [Fact]
    public async Task RenewAsync_re_attributes_the_row_to_the_renewing_users_id()
    {
        using var db = new SqliteTestDatabase();
        var client = NewDb(db);
        var store = new SqlSugarUserSessionStore(client);
        var userA = Guid.CreateVersion7();
        var userB = Guid.CreateVersion7();
        var now = DateTime.UtcNow;
        await store.RecordAsync(userA, "key-1", now, now.AddHours(8));

        var matched = await store.RenewAsync("key-1", userB, now.AddHours(16));

        matched.Should().BeTrue();
        (await store.ListTicketKeysForUserAsync(userB)).Should().Contain("key-1");
        (await store.ListTicketKeysForUserAsync(userA)).Should().NotContain("key-1");
    }

    [Fact]
    public async Task RemoveByTicketKeyAsync_deletes_only_the_matching_row()
    {
        using var db = new SqliteTestDatabase();
        var client = NewDb(db);
        var store = new SqlSugarUserSessionStore(client);
        var userId = Guid.CreateVersion7();
        var now = DateTime.UtcNow;
        await store.RecordAsync(userId, "key-1", now, now.AddHours(8));
        await store.RecordAsync(userId, "key-2", now, now.AddHours(8));

        await store.RemoveByTicketKeyAsync("key-1");

        (await store.ListTicketKeysForUserAsync(userId)).Should().BeEquivalentTo(["key-2"]);
    }

    [Fact]
    public async Task RemoveAllForUserAsync_deletes_every_row_for_that_user_only()
    {
        using var db = new SqliteTestDatabase();
        var client = NewDb(db);
        var store = new SqlSugarUserSessionStore(client);
        var userA = Guid.CreateVersion7();
        var userB = Guid.CreateVersion7();
        var now = DateTime.UtcNow;
        await store.RecordAsync(userA, "a-key-1", now, now.AddHours(8));
        await store.RecordAsync(userA, "a-key-2", now, now.AddHours(8));
        await store.RecordAsync(userB, "b-key", now, now.AddHours(8));

        await store.RemoveAllForUserAsync(userA);

        (await store.ListTicketKeysForUserAsync(userA)).Should().BeEmpty();
        (await store.ListTicketKeysForUserAsync(userB)).Should().BeEquivalentTo(["b-key"]);
    }

    [Fact]
    public async Task RemoveExpiredForUserAsync_deletes_only_expired_rows_for_that_user()
    {
        using var db = new SqliteTestDatabase();
        var client = NewDb(db);
        var store = new SqlSugarUserSessionStore(client);
        var userA = Guid.CreateVersion7();
        var userB = Guid.CreateVersion7();
        var now = DateTime.UtcNow;

        // userA: one expired row, one live row. userB: one expired row that must survive untouched.
        await store.RecordAsync(userA, "a-expired", now.AddHours(-9), now.AddHours(-1));
        await store.RecordAsync(userA, "a-live", now, now.AddHours(8));
        await store.RecordAsync(userB, "b-expired", now.AddHours(-9), now.AddHours(-1));

        await store.RemoveExpiredForUserAsync(userA, now);

        (await store.ListTicketKeysForUserAsync(userA)).Should().BeEquivalentTo(["a-live"]);
        (await store.ListTicketKeysForUserAsync(userB)).Should().BeEquivalentTo(["b-expired"]);
    }
}
