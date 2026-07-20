using System.Net;
using System.Text.Json;
using AwesomeAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Struo.Tests.Support;
using Xunit;

namespace Struo.Tests.Api;

[Collection("ApiIntegration")]
public class ConfigEndpointTests(ApiFactory factory)
{
    private readonly ApiFactory _factory = factory;

    [Fact]
    public async Task Config_is_anonymous_and_returns_defaults()
    {
        var client = _factory.CreateClient(); // no auth
        var response = await client.GetAsync("/api/config");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        doc.RootElement.GetProperty("success").GetBoolean().Should().BeTrue();
        var data = doc.RootElement.GetProperty("data");
        data.GetProperty("oidcEnabled").GetBoolean().Should().BeFalse();
        data.GetProperty("brandName").GetString().Should().Be("StruoCMS");
        data.GetProperty("brandLogoUrl").ValueKind.Should().Be(JsonValueKind.Null);
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
    public async Task Config_reflects_oidc_enabled()
    {
        var f = _factory.WithWebHostBuilder(b =>
            b.ConfigureAppConfiguration((_, c) => c.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Oidc:Enabled"] = "true",
                ["Oidc:Authority"] = "https://login.example.com",
                ["Oidc:ClientId"] = "test-client",
                ["Oidc:ClientSecret"] = "test-secret",
            })));
        var response = await f.CreateClient().GetAsync("/api/config");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        doc.RootElement.GetProperty("data").GetProperty("oidcEnabled").GetBoolean().Should().BeTrue();
    }
}
