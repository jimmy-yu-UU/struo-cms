using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using System.Text.Json;
using AwesomeAssertions;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Struo.Api.Controllers;
using Struo.Application.Settings;
using Struo.Infrastructure.Settings;
using SqlSugar;
using Struo.Tests.Support;
using Xunit;

namespace Struo.Tests.Api;

[Collection("ApiIntegration")]
public class ConfigBrandingTests(ApiFactory factory)
{
    private readonly ApiFactory _factory = factory;

    private async Task SeedAsync(string name, Guid? logo)
    {
        using var scope = _factory.Services.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<ISiteSettingsStore>();
        await store.UpsertAsync(name, logo, null, default);
        // This bypasses SettingsController.UpdateBranding (the only production write path,
        // which evicts the cache itself) — evict here too so the shared ApiFactory's /api/config
        // cache doesn't serve a stale pre-seed value to this or a later test in the collection.
        scope.ServiceProvider.GetRequiredService<IMemoryCache>().Remove(ConfigController.CacheKey);
    }

    private async Task ClearAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ISqlSugarClient>();
        await db.Deleteable<SiteSettings>().Where(s => s.Id == SiteSettings.SingletonId).ExecuteCommandAsync();
        scope.ServiceProvider.GetRequiredService<IMemoryCache>().Remove(ConfigController.CacheKey);
    }

    /// <summary>Mirrors SettingsControllerTests.SeedFileAsync: seeds a minimal
    /// <see cref="Struo.Infrastructure.Files.File"/> row with the given status.</summary>
    private async Task<Guid> SeedFileAsync(string status)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ISqlSugarClient>();
        var id = Guid.NewGuid();
        await db.Insertable(new Struo.Infrastructure.Files.File
        {
            Id = id, StorageKey = "k", FileName = "logo.png", ContentType = "image/png",
            Size = 1, Status = status,
        }).ExecuteCommandAsync();
        return id;
    }

    private async Task DeleteFileAsync(Guid id)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ISqlSugarClient>();
        await db.Deleteable<Struo.Infrastructure.Files.File>().Where(f => f.Id == id).ExecuteCommandAsync();
    }

    [Fact]
    public async Task Config_reflects_saved_brand_name_and_logo_file()
    {
        // ConfigController re-verifies the file still exists AND is published before
        // emitting its URL, so this must seed a real published File row (not just a bare Guid).
        var logo = await SeedFileAsync("published");
        await SeedAsync("Saved Brand", logo);
        try
        {
            using var doc = JsonDocument.Parse(
                await (await _factory.CreateClient().GetAsync("/api/config")).Content.ReadAsStringAsync());
            var data = doc.RootElement.GetProperty("data");
            data.GetProperty("brandName").GetString().Should().Be("Saved Brand");
            data.GetProperty("brandLogoUrl").GetString().Should().Be($"/api/files/{logo}/content");
        }
        finally
        {
            await ClearAsync();
            await DeleteFileAsync(logo);
        }
    }

    [Fact]
    public async Task Config_falls_back_to_appsettings_logo_when_no_logo_file()
    {
        await SeedAsync("Name Only", null);
        try
        {
            using var doc = JsonDocument.Parse(
                await (await _factory.CreateClient().GetAsync("/api/config")).Content.ReadAsStringAsync());
            var data = doc.RootElement.GetProperty("data");
            data.GetProperty("brandName").GetString().Should().Be("Name Only");
            // No appsettings LogoUrl configured in the default test host → null.
            data.GetProperty("brandLogoUrl").ValueKind.Should().Be(JsonValueKind.Null);
        }
        finally { await ClearAsync(); }
    }

    /// <summary>The logo file referenced by site_settings was deleted after being saved
    /// (the classic TOCTOU) — ConfigController must fall back to the appsettings default instead
    /// of emitting a URL for a deleted file.</summary>
    [Fact]
    public async Task Config_falls_back_to_appsettings_logo_when_referenced_file_is_missing()
    {
        var logo = await SeedFileAsync("published");
        await SeedAsync("Ghost Logo Brand", logo);
        await DeleteFileAsync(logo); // simulate the file being deleted independently of settings

        var f = _factory.WithWebHostBuilder(b =>
            b.ConfigureAppConfiguration((_, c) => c.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Branding:LogoUrl"] = "https://cdn.example.com/fallback-logo.svg",
            })));
        try
        {
            using var doc = JsonDocument.Parse(
                await (await f.CreateClient().GetAsync("/api/config")).Content.ReadAsStringAsync());
            var data = doc.RootElement.GetProperty("data");
            data.GetProperty("brandName").GetString().Should().Be("Ghost Logo Brand");
            data.GetProperty("brandLogoUrl").GetString().Should().Be("https://cdn.example.com/fallback-logo.svg");
        }
        finally { await ClearAsync(); }
    }

    /// <summary>The logo file referenced by site_settings was unpublished after being
    /// saved — ConfigController must fall back rather than serve an inaccessible/unpublished
    /// file's URL to the anonymous login page.</summary>
    [Fact]
    public async Task Config_falls_back_to_appsettings_logo_when_referenced_file_is_unpublished()
    {
        var logo = await SeedFileAsync("published");
        await SeedAsync("Unpublished Logo Brand", logo);

        // Flip the file to unpublished independently of settings (e.g. an editor unpublished it later).
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ISqlSugarClient>();
            await db.Updateable<Struo.Infrastructure.Files.File>()
                .SetColumns(fl => new Struo.Infrastructure.Files.File { Status = "draft" })
                .Where(fl => fl.Id == logo)
                .ExecuteCommandAsync();
        }

        var f = _factory.WithWebHostBuilder(b =>
            b.ConfigureAppConfiguration((_, c) => c.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Branding:LogoUrl"] = "https://cdn.example.com/fallback-logo.svg",
            })));
        try
        {
            using var doc = JsonDocument.Parse(
                await (await f.CreateClient().GetAsync("/api/config")).Content.ReadAsStringAsync());
            var data = doc.RootElement.GetProperty("data");
            data.GetProperty("brandName").GetString().Should().Be("Unpublished Logo Brand");
            data.GetProperty("brandLogoUrl").GetString().Should().Be("https://cdn.example.com/fallback-logo.svg");
        }
        finally
        {
            await ClearAsync();
            await DeleteFileAsync(logo);
        }
    }
}
