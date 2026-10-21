using System.Net;
using System.Text.Json;
using AwesomeAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SqlSugar;
using Struo.Api.Controllers;
using Struo.Application.Settings;
using Struo.Infrastructure.Settings;
using Struo.Tests.Support;
using Xunit;

namespace Struo.Tests.Api;

[Collection("ApiIntegration")]
public class ConfigEndpointTests(ApiFactory factory)
{
    private readonly ApiFactory _factory = factory;

    private async Task ClearAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ISqlSugarClient>();
        await db.Deleteable<SiteSettings>().Where(s => s.Id == SiteSettings.SingletonId).ExecuteCommandAsync();
        scope.ServiceProvider.GetRequiredService<IMemoryCache>().Remove(ConfigController.CacheKey);
    }

    [Fact]
    public async Task Config_is_anonymous_and_returns_defaults()
    {
        // Self-contained — don't rely on another test's teardown to leave a clean singleton
        // row (and evicted cache) behind; clear it here too.
        await ClearAsync();
        var client = _factory.CreateClient(); // no auth
        var response = await client.GetAsync("/api/config");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        doc.RootElement.GetProperty("success").GetBoolean().Should().BeTrue();
        var data = doc.RootElement.GetProperty("data");
        data.GetProperty("oidcProviders").GetArrayLength().Should().Be(0);
        data.TryGetProperty("oidcEnabled", out _).Should().BeFalse();
        data.GetProperty("brandName").GetString().Should().Be("StruoCMS");
        data.GetProperty("brandLogoUrl").ValueKind.Should().Be(JsonValueKind.Null);
        data.TryGetProperty("uiLocales", out _).Should().BeFalse();
        data.TryGetProperty("uiDefaultLocale", out _).Should().BeFalse();
    }

    [Fact]
    public async Task Config_reflects_configured_branding()
    {
        var f = _factory.WithWebHostBuilder(b =>
            b.ConfigureAppConfiguration((_, c) => c.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Branding:Name"] = "Acme Docs",
                ["Branding:LogoUrl"] = "https://cdn.example.com/logo.svg",
            })));
        var response = await f.CreateClient().GetAsync("/api/config");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var data = doc.RootElement.GetProperty("data");
        data.GetProperty("brandName").GetString().Should().Be("Acme Docs");
        data.GetProperty("brandLogoUrl").GetString().Should().Be("https://cdn.example.com/logo.svg");
    }

    [Fact]
    public async Task Config_lists_enabled_oidc_providers_in_Order_without_secrets()
    {
        var f = _factory.WithWebHostBuilder(b =>
            b.ConfigureAppConfiguration((_, c) => c.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Oidc:Providers:microsoft:Enabled"] = "true",
                ["Oidc:Providers:microsoft:DisplayName"] = "Microsoft 365",
                ["Oidc:Providers:microsoft:Icon"] = "microsoft",
                ["Oidc:Providers:microsoft:Authority"] = "https://login.example.com/tenant/v2.0",
                ["Oidc:Providers:microsoft:ClientId"] = "ms-client",
                ["Oidc:Providers:microsoft:ClientSecret"] = "ms-secret",
                ["Oidc:Providers:microsoft:Order"] = "2",
                ["Oidc:Providers:google:Enabled"] = "true",
                ["Oidc:Providers:google:DisplayName"] = "Google",
                ["Oidc:Providers:google:Icon"] = "google",
                ["Oidc:Providers:google:Authority"] = "https://accounts.example.com",
                ["Oidc:Providers:google:ClientId"] = "g-client",
                ["Oidc:Providers:google:ClientSecret"] = "g-secret",
                ["Oidc:Providers:google:Order"] = "1",
            })));
        var response = await f.CreateClient().GetAsync("/api/config");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadAsStringAsync();
        body.Should().NotContain("secret").And.NotContain("client");
        using var doc = JsonDocument.Parse(body);
        var providers = doc.RootElement.GetProperty("data").GetProperty("oidcProviders").EnumerateArray().ToList();
        providers.Select(p => p.GetProperty("id").GetString()).Should().Equal("google", "microsoft");
        providers[0].GetProperty("displayName").GetString().Should().Be("Google");
        providers[1].GetProperty("icon").GetString().Should().Be("microsoft");
        providers[0].EnumerateObject().Select(p => p.Name).Should().BeEquivalentTo("id", "displayName", "icon");
    }

    /// <summary><see cref="ISiteSettingsStore"/> itself does not validate
    /// blankness — a future writer/seeding path could persist a blank BrandName — so
    /// <see cref="ConfigController"/> must fall back to the appsettings default rather than surface
    /// the blank value.</summary>
    [Fact]
    public async Task Config_falls_back_to_default_when_saved_brand_name_is_blank()
    {
        using var scope = _factory.Services.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<ISiteSettingsStore>();
        try
        {
            await store.UpsertAsync("   ", null, null);
            // Precondition: the store persists the blank verbatim (no trim/reject). If it ever gains
            // blank-rejection this assert fails loudly instead of the test silently going vacuous
            // (a null row would ALSO yield the "StruoCMS" default below).
            (await store.GetAsync())!.BrandName.Should().Be("   ");
            scope.ServiceProvider.GetRequiredService<IMemoryCache>().Remove(ConfigController.CacheKey);

            var response = await _factory.CreateClient().GetAsync("/api/config");
            response.StatusCode.Should().Be(HttpStatusCode.OK);

            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            doc.RootElement.GetProperty("data").GetProperty("brandName").GetString().Should().Be("StruoCMS");
        }
        finally { await ClearAsync(); }
    }
}
