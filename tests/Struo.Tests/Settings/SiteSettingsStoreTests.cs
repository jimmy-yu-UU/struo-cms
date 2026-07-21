using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using SqlSugar;
using Struo.Application.Settings;
using Struo.Infrastructure.Settings;
using Struo.Tests.Support;
using Xunit;

namespace Struo.Tests.Settings;

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
            await WithStoreAsync<object?>(async (_, db) =>
            {
                await db.Deleteable<SiteSettings>().Where(s => s.Id == SiteSettings.SingletonId).ExecuteCommandAsync();
                return null;
            });
        }
    }

    [Fact]
    public async Task Get_returns_null_when_no_row()
    {
        await WithStoreAsync<object?>(async (_, db) =>
        {
            await db.Deleteable<SiteSettings>().Where(s => s.Id == SiteSettings.SingletonId).ExecuteCommandAsync();
            return null;
        });
        var rec = await WithStoreAsync((store, _) => store.GetAsync(default));
        rec.Should().BeNull();
    }
}
