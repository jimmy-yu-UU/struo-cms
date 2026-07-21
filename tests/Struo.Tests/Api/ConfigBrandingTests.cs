using System.Net;
using System.Text.Json;
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
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
    }

    private async Task ClearAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ISqlSugarClient>();
        await db.Deleteable<SiteSettings>().Where(s => s.Id == SiteSettings.SingletonId).ExecuteCommandAsync();
    }

    [Fact]
    public async Task Config_reflects_saved_brand_name_and_logo_file()
    {
        var logo = Guid.CreateVersion7();
        await SeedAsync("Saved Brand", logo);
        try
        {
            using var doc = JsonDocument.Parse(
                await (await _factory.CreateClient().GetAsync("/api/config")).Content.ReadAsStringAsync());
            var data = doc.RootElement.GetProperty("data");
            data.GetProperty("brandName").GetString().Should().Be("Saved Brand");
            data.GetProperty("brandLogoUrl").GetString().Should().Be($"/api/files/{logo}/content");
        }
        finally { await ClearAsync(); }
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
}
