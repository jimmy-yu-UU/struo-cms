// tests/Struo.Tests/Query/StaleQueryOptionKeyTests.cs
using System.Collections.Generic;
using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using AwesomeAssertions;
using Microsoft.Extensions.Configuration;
using Struo.Api.Auth;
using Struo.Tests.Support;
using Xunit;

namespace Struo.Tests.Query;

// StruoQueryOptions is bound with ValidateOnStart + [Range] attributes. It has no
// resolved-id-set cap key: cross-relation filters and translatable search are
// answered by SQL pushdown rather than a materialized, cap-checked id set. A fork or an old
// deployment's appsettings/env may still carry that stale key around; the default configuration
// binder ignores config keys with no matching property, so it must not block startup or options
// validation. Proven end-to-end: a derived host configured with the stale key still starts and
// answers a real authenticated request.
//
// NOTE: WithWebHostBuilder returns a WebApplicationFactory<Program> (an internal
// DelegatedWebApplicationFactory under the hood, NOT an ApiFactory) — casting it back to ApiFactory
// compiles but throws at runtime (see PasswordChangeRateLimitTests). Both hosts share the ONE
// SqliteTestDatabase owned by the base ApiFactory, so the admin row is seeded through the base
// factory and logged in against the DERIVED host's own HttpClient/cookie jar.
[Collection("ApiIntegration")]
public class StaleQueryOptionKeyTests(ApiFactory factory)
{
    private readonly ApiFactory _factory = factory;

    [Fact]
    public async Task Stale_MaxResolvedFilterIds_key_does_not_block_startup()
    {
        WebApplicationFactory<Program> host = _factory.WithWebHostBuilder(b =>
            b.ConfigureAppConfiguration((_, c) => c.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Query:MaxResolvedFilterIds"] = "5000",
            })));

        await _factory.EnsureAdminSeededAsync();
        var client = host.CreateClient();
        client.DefaultRequestHeaders.Add(CsrfProtectionMiddleware.HeaderName, "1");
        var login = await client.PostAsJsonAsync("/api/auth/login",
            new { email = ApiFactory.AdminEmail, password = ApiFactory.AdminPassword });
        login.EnsureSuccessStatusCode();

        var response = await client.GetAsync("/api/items/article");

        response.StatusCode.Should().Be(HttpStatusCode.OK,
            "the host must start and answer requests even with a stale, no-longer-bound " +
            "Query:MaxResolvedFilterIds key in configuration — the default binder ignores unknown keys");
    }
}
