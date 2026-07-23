using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AwesomeAssertions;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using SqlSugar;
using Struo.Api.Controllers;
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
        // SEC-7: the successful PUT already evicted /api/config's cache, but this raw cleanup
        // delete does not — evict again so a later test in the shared collection never observes a
        // stale cached branding value from this test's teardown.
        scope.ServiceProvider.GetRequiredService<IMemoryCache>().Remove(ConfigController.CacheKey);
    }

    /// <summary>Seeds a minimal <see cref="Struo.Infrastructure.Files.File"/> row, mirroring the
    /// insert idiom used by FileServiceTransactionTests / ExternalLoginProvisioningTests (scoped
    /// <see cref="ISqlSugarClient"/> off the DI container, not the unit-level SqliteTestDatabase).</summary>
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
    public async Task Put_requires_super_admin()
    {
        var (client, _) = await _factory.CreateRolelessClientAsync();
        var resp = await client.PutAsJsonAsync("/api/settings/branding", new { brandName = "X", logoFileId = (string?)null });
        resp.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        // TEST-7: pin the actual code SettingsController.UpdateBranding returns for this branch
        // (ErrorCodes.Forbidden), not just the status code.
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
        doc.RootElement.GetProperty("error").GetProperty("code").GetString().Should().Be("FORBIDDEN");
    }

    /// <summary>TEST-3 (audit batch 4): anonymous callers get 401, not 403 CSRF. Deliberately sent
    /// WITHOUT the X-Struo-CSRF header, matching a real anonymous SPA caller. CSRF is not in play for
    /// two independent reasons: (1) UseAuthorization runs BEFORE CsrfProtectionMiddleware in the
    /// pipeline (Program.cs), so an unauthorized request 401s before the CSRF gate is even reached;
    /// and (2) even were the order reversed, CsrfProtectionMiddleware only demands the header when the
    /// request carries the session cookie (<see cref="Struo.Api.Auth.CsrfProtectionMiddleware"/>), which
    /// an anonymous request lacks. Either way the request falls through to the [Authorize] challenge.
    ///
    /// FIXED (AUTH-1, audit batch 4 follow-up): the challenge is 401 as expected, and now carries
    /// an error envelope. Cookie auth's <c>OnRedirectToLogin</c> event (AuthWiring.cs) still sets
    /// <c>Response.StatusCode</c> directly and short-circuits before MVC's
    /// <see cref="Struo.Api.Http.EnvelopeResultFilter"/> ever runs — but it now also writes the same
    /// envelope shape the filter would have produced, so [Authorize]-attribute-level 401s and
    /// in-action-exception 401s (covered by UnauthorizedDriftTests, thrown PermissionDeniedException
    /// mapped by DomainErrorMap) agree on <c>error.code</c>.</summary>
    [Fact]
    public async Task Put_anonymous_is_401_with_unauthorized_envelope()
    {
        var client = _factory.CreateClient(); // anonymous, no cookie, no CSRF header
        var resp = await client.PutAsJsonAsync("/api/settings/branding", new { brandName = "X", logoFileId = (string?)null });
        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
        doc.RootElement.GetProperty("error").GetProperty("code").GetString().Should().Be("UNAUTHORIZED");
    }

    [Fact]
    public async Task Put_rejects_empty_name()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();
        var resp = await client.PutAsJsonAsync("/api/settings/branding", new { brandName = "   ", logoFileId = (string?)null });
        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        // TEST-7: SettingsController.UpdateBranding maps this branch to ErrorCodes.BadUserInput.
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
        doc.RootElement.GetProperty("error").GetProperty("code").GetString().Should().Be("BAD_USER_INPUT");
    }

    [Fact]
    public async Task Put_rejects_name_over_100_chars()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();
        var resp = await client.PutAsJsonAsync("/api/settings/branding",
            new { brandName = new string('a', 101), logoFileId = (string?)null });
        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
        doc.RootElement.GetProperty("error").GetProperty("code").GetString().Should().Be("BAD_USER_INPUT");
    }

    [Fact]
    public async Task Put_rejects_unknown_logo_file()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();
        var resp = await client.PutAsJsonAsync("/api/settings/branding",
            new { brandName = "Brand", logoFileId = Guid.NewGuid() });
        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
        doc.RootElement.GetProperty("error").GetProperty("code").GetString().Should().Be("BAD_USER_INPUT");
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

    // TEST-9: pins the ACTUAL behavior read off SettingsController.UpdateBranding —
    // `body.BrandName?.Trim()` — so surrounding whitespace is stripped before it is persisted and
    // echoed back, rather than being preserved verbatim.
    [Fact]
    public async Task Put_trims_surrounding_whitespace_from_brand_name()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();
        try
        {
            var resp = await client.PutAsJsonAsync("/api/settings/branding",
                new { brandName = "  My Brand  ", logoFileId = (string?)null });
            resp.StatusCode.Should().Be(HttpStatusCode.OK);
            using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
            doc.RootElement.GetProperty("data").GetProperty("brandName").GetString().Should().Be("My Brand");

            using var cfg = JsonDocument.Parse(
                await (await _factory.CreateClient().GetAsync("/api/config")).Content.ReadAsStringAsync());
            cfg.RootElement.GetProperty("data").GetProperty("brandName").GetString().Should().Be("My Brand");
        }
        finally { await ClearAsync(); }
    }

    [Fact]
    public async Task Put_with_published_logo_file_sets_brandLogoUrl()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();
        var fileId = await SeedFileAsync("published");
        try
        {
            var resp = await client.PutAsJsonAsync("/api/settings/branding",
                new { brandName = "Logo Brand", logoFileId = fileId });
            resp.StatusCode.Should().Be(HttpStatusCode.OK);
            using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
            var data = doc.RootElement.GetProperty("data");
            data.GetProperty("brandLogoUrl").GetString().Should().Be($"/api/files/{fileId}/content");
        }
        finally
        {
            await ClearAsync();
            await DeleteFileAsync(fileId);
        }
    }

    [Fact]
    public async Task Put_rejects_unpublished_logo_file()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();
        var fileId = await SeedFileAsync("draft");
        try
        {
            var resp = await client.PutAsJsonAsync("/api/settings/branding",
                new { brandName = "Draft Logo Brand", logoFileId = fileId });
            resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        }
        finally
        {
            await ClearAsync();
            await DeleteFileAsync(fileId);
        }
    }
}
