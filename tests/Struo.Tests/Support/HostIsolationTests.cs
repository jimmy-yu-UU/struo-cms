// tests/Struo.Tests/Support/HostIsolationTests.cs
using AwesomeAssertions;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Struo.Application.Security;
using Struo.Infrastructure.Files;
using Xunit;

namespace Struo.Tests.Support;

/// <summary>
/// A test host that loads the gitignored Development settings file must not reach the maintainer's real
/// Redis or S3/MinIO bucket. The assertions look at what the host wired, never at the configured values.
/// </summary>
[Collection("ApiIntegration")]
public class HostIsolationTests(ApiFactory factory)
{
    private static void AssertNoExternalTargets(IServiceProvider services)
    {
        var redisConfigured = !string.IsNullOrWhiteSpace(
            services.GetRequiredService<IConfiguration>()["Redis:ConnectionString"]);
        redisConfigured.Should().BeFalse("a test host must fall back to the in-memory session store");

        using var scope = services.CreateScope();
        var cacheType = scope.ServiceProvider.GetRequiredService<IDistributedCache>().GetType().FullName ?? "";
        cacheType.Contains("Redis", StringComparison.OrdinalIgnoreCase).Should().BeFalse("the session store must not be Redis");
        var throttleType = services.GetRequiredService<ILoginAttemptThrottle>().GetType().Name;
        throttleType.Contains("Redis", StringComparison.OrdinalIgnoreCase).Should().BeFalse("the login throttle must not be Redis");

        var storage = services.GetRequiredService<Struo.Application.Files.IFileStorage>();
        (storage is LocalFileStorage).Should().BeTrue("the file backend must be the local one");
        (storage is S3FileStorage).Should().BeFalse("no test host may talk to a remote bucket");
    }

    [Fact]
    public void The_default_ApiFactory_host_wires_no_Redis_and_no_remote_file_backend() =>
        AssertNoExternalTargets(factory.Services);

    [SqliteOnlyFact("starts a full host on a SQLite temp database")]
    public async Task A_ForkHost_wires_no_Redis_and_no_remote_file_backend()
    {
        await using var host = await ForkHost.StartAsync();

        AssertNoExternalTargets(host.App.Services);
    }
}
