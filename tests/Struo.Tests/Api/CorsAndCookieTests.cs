// tests/Struo.Tests/Api/CorsAndCookieTests.cs
using System.Net.Http.Json;
using AwesomeAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Struo.Tests.Support;
using Xunit;

namespace Struo.Tests.Api;

// Not part of the ApiFactory fixture (this class builds its own WebApplicationFactory<Program>
// variant per the task brief), but still placed in the "ApiIntegration" collection to serialize
// against it. xUnit runs different collections in parallel by default, and WebApplicationFactory<Program>
// relies on process-wide static state (HostFactoryResolver's diagnostic listener) during startup that
// is not safe for concurrent factory instances of the same entry point — running this class unpinned
// caused an intermittent but reproducible "entry point exited without ever building an IHost" failure
// in both this class and the shared ApiFactory when both started concurrently.
[Collection("ApiIntegration")]
public class CorsAndCookieTests
{
    private const string Origin = "https://admin.example.test";

    // A factory variant that enables cross-origin mode via config.
    private sealed class CorsApiFactory : WebApplicationFactory<Program>
    {
        private readonly SqliteTestDatabase _db = new();
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development");
            builder.ConfigureAppConfiguration((_, config) =>
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Database:DbType"] = "Sqlite",
                    ["Database:ConnectionString"] = _db.ConnectionString,
                    // Struo:ContentAssemblies cannot be set here - it is read before Build(). See Support/ContentAssemblyEnvBootstrap.cs.
                    ["Struo:Cors:AllowedOrigins:0"] = "https://admin.example.test"
                }));
        }
        protected override void Dispose(bool disposing) { base.Dispose(disposing); if (disposing) _db.Dispose(); }
    }

    [Fact]
    public async Task Cors_enabled_preflight_allows_configured_origin_with_credentials()
    {
        using var factory = new CorsApiFactory();
        var client = factory.CreateClient();
        var req = new HttpRequestMessage(HttpMethod.Options, "/api/auth/me");
        req.Headers.Add("Origin", Origin);
        req.Headers.Add("Access-Control-Request-Method", "GET");
        var resp = await client.SendAsync(req);

        resp.Headers.GetValues("Access-Control-Allow-Origin").Should().Contain(Origin);
        resp.Headers.GetValues("Access-Control-Allow-Credentials").Should().Contain("true");
    }

    [Fact]
    public async Task Cors_enabled_login_sets_samesite_none_secure_cookie()
    {
        using var factory = new CorsApiFactory();
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SqlSugar.ISqlSugarClient>();
            var hasher = scope.ServiceProvider.GetRequiredService<Struo.Application.Security.IPasswordHasher>();
            await db.Insertable(new Struo.Infrastructure.Identity.User
            {
                Id = Guid.CreateVersion7(), Email = "cors@b.com",
                Password = hasher.Hash("pw12345678"), IsActive = true
            }).ExecuteCommandAsync();
        }
        var client = factory.CreateClient();
        var resp = await client.PostAsJsonAsync("/api/auth/login", new { email = "cors@b.com", password = "pw12345678" });
        var setCookie = string.Join(";", resp.Headers.GetValues("Set-Cookie"));
        setCookie.ToLowerInvariant().Should().Contain("samesite=none");
        setCookie.ToLowerInvariant().Should().Contain("secure");
    }
}
