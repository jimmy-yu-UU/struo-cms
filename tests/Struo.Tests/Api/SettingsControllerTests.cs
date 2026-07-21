using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using SqlSugar;
using Struo.Infrastructure.Settings;
using Struo.Tests.Support;
using Xunit;

namespace Struo.Tests.Api;

[Collection("ApiIntegration")]
public class SettingsControllerTests(ApiFactory factory)
{
    private readonly ApiFactory _factory = factory;

    private async Task ClearAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ISqlSugarClient>();
        await db.Deleteable<SiteSettings>().Where(s => s.Id == SiteSettings.SingletonId).ExecuteCommandAsync();
    }

    [Fact]
    public async Task Put_requires_super_admin()
    {
        var (client, _) = await _factory.CreateRolelessClientAsync();
        var resp = await client.PutAsJsonAsync("/api/settings/branding", new { brandName = "X", logoFileId = (string?)null });
        resp.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Put_rejects_empty_name()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();
        var resp = await client.PutAsJsonAsync("/api/settings/branding", new { brandName = "   ", logoFileId = (string?)null });
        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Put_rejects_name_over_100_chars()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();
        var resp = await client.PutAsJsonAsync("/api/settings/branding",
            new { brandName = new string('a', 101), logoFileId = (string?)null });
        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Put_rejects_unknown_logo_file()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();
        var resp = await client.PutAsJsonAsync("/api/settings/branding",
            new { brandName = "Brand", logoFileId = Guid.NewGuid() });
        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Put_saves_and_returns_effective_config()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();
        try
        {
            var resp = await client.PutAsJsonAsync("/api/settings/branding",
                new { brandName = "My Brand", logoFileId = (string?)null });
            resp.StatusCode.Should().Be(HttpStatusCode.OK);
            using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
            var data = doc.RootElement.GetProperty("data");
            data.GetProperty("brandName").GetString().Should().Be("My Brand");

            // Anonymous /api/config now reflects the saved name.
            using var cfg = JsonDocument.Parse(
                await (await _factory.CreateClient().GetAsync("/api/config")).Content.ReadAsStringAsync());
            cfg.RootElement.GetProperty("data").GetProperty("brandName").GetString().Should().Be("My Brand");
        }
        finally { await ClearAsync(); }
    }
}
