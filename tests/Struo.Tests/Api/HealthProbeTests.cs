// tests/Struo.Tests/Api/HealthProbeTests.cs
using System.Net;
using AwesomeAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Struo.Application.Security;
using Struo.Tests.Support;
using Xunit;

namespace Struo.Tests.Api;

/// <summary>
/// The liveness and readiness probes must not depend on permission resolution: a role/permission
/// store failure must not turn <c>/health/live</c> into a 500 that makes an orchestrator restart
/// healthy pods. Own derived host (throwing <see cref="IRolePermissionStore"/>) so the shared
/// ApiFactory fixture keeps a working store.
/// </summary>
[Collection("ApiIntegration")]
public class HealthProbeTests(ApiFactory factory)
{
    private readonly ApiFactory _factory = factory;

    private sealed class ThrowingRolePermissionStore : IRolePermissionStore
    {
        public Task<RolePermissionData> LoadForUserAsync(Guid? userId, CancellationToken ct = default) =>
            throw new InvalidOperationException("permission store unreachable");

        public Task<RolePermissionData> LoadForRolesAsync(IReadOnlyList<Guid> roleIds, CancellationToken ct = default) =>
            throw new InvalidOperationException("permission store unreachable");
    }

    private HttpClient CreateClientWithFailingPermissionStore() =>
        _factory
            .WithWebHostBuilder(b => b.ConfigureServices(s =>
                s.AddScoped<IRolePermissionStore, ThrowingRolePermissionStore>()))
            .CreateClient();

    [Theory]
    [InlineData("/health/live")]
    [InlineData("/health/ready")]
    public async Task Health_probes_succeed_when_permission_resolution_would_fail(string path)
    {
        var client = CreateClientWithFailingPermissionStore();

        var response = await client.GetAsync(path);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Api_routes_still_resolve_permissions_through_the_store()
    {
        var client = CreateClientWithFailingPermissionStore();

        var response = await client.GetAsync("/api/ping");

        response.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
    }

    [Theory]
    [InlineData("/healthz")]
    [InlineData("/health-foo")]
    public async Task Paths_that_only_share_the_health_prefix_do_not_skip_resolution(string path)
    {
        var client = CreateClientWithFailingPermissionStore();

        var response = await client.GetAsync(path);

        response.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
    }
}
