using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using SqlSugar;
using Struo.Application.Settings;
using Struo.Infrastructure.Settings;
using Struo.Tests.Support;
using Xunit;

namespace Struo.Tests.Settings;

/// <summary>
/// Covers <see cref="Struo.Infrastructure.Settings.SqlSugarSiteSettingsStore"/>'s
/// insert / update / single-row invariants against the SQLite test provider. NOT covered here — and
/// requiring a live-PG gate instead — is the actual concurrent-PUT race this store's upsert closes: two
/// overlapping first-saves racing to insert the singleton row and one losing with a Postgres 23505
/// unique_violation. The test harness's single SQLite connection serializes everything, so it cannot
/// physically produce that race; a live-PG gate should fire two concurrent <c>UpsertAsync</c> calls
/// against an empty <c>site_settings</c> table and assert both complete without throwing and exactly one
/// row remains.
/// </summary>
[Collection("ApiIntegration")]
public class SiteSettingsStoreTests(ApiFactory factory)
{
    private readonly ApiFactory _factory = factory;

    private async Task<T> WithStoreAsync<T>(Func<ISiteSettingsStore, ISqlSugarClient, Task<T>> body)
    {
        using var scope = _factory.Services.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<ISiteSettingsStore>();
        var db = scope.ServiceProvider.GetRequiredService<ISqlSugarClient>();
        return await body(store, db);
    }

    private static async Task<object?> ClearAsync(ISqlSugarClient db)
    {
        await db.Deleteable<SiteSettings>().Where(s => s.Id == SiteSettings.SingletonId).ExecuteCommandAsync();
        return null;
    }

    [Fact]
    public async Task Upsert_inserts_then_updates_a_single_row()
    {
        var logo = Guid.CreateVersion7();
        try
        {
            await WithStoreAsync<object?>(async (store, db) =>
            {
                await store.UpsertAsync("Acme", logo, null, default);
                await store.UpsertAsync("Acme 2", null, null, default);

                var count = await db.Queryable<SiteSettings>()
                    .Where(s => s.Id == SiteSettings.SingletonId).CountAsync();
                count.Should().Be(1);

                var rec = await store.GetAsync(default);
                rec.Should().NotBeNull();
                rec!.BrandName.Should().Be("Acme 2");
                rec.LogoFileId.Should().BeNull();
                return null;
            });
        }
        finally
        {
            await WithStoreAsync<object?>((_, db) => ClearAsync(db));
        }
    }

    // Pins the "no row -> insert" branch of the UPDATE-first/INSERT-on-miss/retry-on-conflict
    // upsert in isolation (the combined test above already exercises insert-then-update together).
    [Fact]
    public async Task Upsert_inserts_a_new_row_when_none_exists()
    {
        try
        {
            await WithStoreAsync<object?>(async (store, db) =>
            {
                await ClearAsync(db);

                await store.UpsertAsync("Acme", null, null, default);

                var row = await db.Queryable<SiteSettings>()
                    .Where(s => s.Id == SiteSettings.SingletonId).FirstAsync();
                row.Should().NotBeNull();
                row!.BrandName.Should().Be("Acme");

                var count = await db.Queryable<SiteSettings>()
                    .Where(s => s.Id == SiteSettings.SingletonId).CountAsync();
                count.Should().Be(1);
                return null;
            });
        }
        finally
        {
            await WithStoreAsync<object?>((_, db) => ClearAsync(db));
        }
    }

    // Pins the "row exists -> update in place" branch (no duplicate row, singleton Id preserved,
    // fields overwritten) — the UPDATE-first path that must NOT fall through to an insert attempt.
    [Fact]
    public async Task Upsert_updates_the_existing_row_in_place_without_duplicating_it()
    {
        var firstLogo = Guid.CreateVersion7();
        var secondLogo = Guid.CreateVersion7();
        try
        {
            await WithStoreAsync<object?>(async (store, db) =>
            {
                await store.UpsertAsync("Acme", firstLogo, null, default);
                var updatedBy = Guid.CreateVersion7();

                await store.UpsertAsync("Acme Renamed", secondLogo, updatedBy, default);

                var row = await db.Queryable<SiteSettings>()
                    .Where(s => s.Id == SiteSettings.SingletonId).FirstAsync();
                row.Should().NotBeNull();
                row!.Id.Should().Be(SiteSettings.SingletonId);
                row.BrandName.Should().Be("Acme Renamed");
                row.LogoFileId.Should().Be(secondLogo);
                row.UpdatedBy.Should().Be(updatedBy);
                // Round-trips through the timestamptz-mapped column without throwing (the Postgres
                // Kind=Utc requirement for `timestamptz` writes is a live-PG-only concern — see class docs).
                row.UpdatedAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromMinutes(1));

                var count = await db.Queryable<SiteSettings>()
                    .Where(s => s.Id == SiteSettings.SingletonId).CountAsync();
                count.Should().Be(1);
                return null;
            });
        }
        finally
        {
            await WithStoreAsync<object?>((_, db) => ClearAsync(db));
        }
    }

    [Fact]
    public async Task Get_returns_null_when_no_row()
    {
        await WithStoreAsync<object?>((_, db) => ClearAsync(db));
        var rec = await WithStoreAsync((store, _) => store.GetAsync(default));
        rec.Should().BeNull();
    }
}
