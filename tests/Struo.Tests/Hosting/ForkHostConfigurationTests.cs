using AwesomeAssertions;
using Struo.Tests.Support;
using Xunit;

namespace Struo.Tests.Hosting;

/// <summary>
/// A <c>ForkHost</c> in Development never reads the developer's user secrets, which hold real connections.
/// Only the provider types and file names are inspected; no configuration value is read.
/// </summary>
[Collection("ApiIntegration")]
public sealed class ForkHostConfigurationTests
{
    [SqliteOnlyFact("starts a host on a SQLite temp database")]
    public async Task A_ForkHost_in_Development_has_no_user_secrets_source()
    {
        await using var host = await ForkHost.StartAsync(environment: "Development");

        HostIsolation.UserSecretsSources(host.App.Configuration).Should().BeEmpty();
    }

    [SqliteOnlyFact("starts a host on a SQLite temp database")]
    public async Task A_ForkHost_still_serves_the_framework_controllers()
    {
        await using var host = await ForkHost.StartAsync(environment: "Production");

        var response = await host.CreateClient().GetAsync("/api/auth/login");

        response.StatusCode.Should().NotBe(System.Net.HttpStatusCode.NotFound);
    }
}
