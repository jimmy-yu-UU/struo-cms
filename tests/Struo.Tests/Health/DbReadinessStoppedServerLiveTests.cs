using System.Diagnostics;
using AwesomeAssertions;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using Struo.Application.Configuration;
using Struo.Infrastructure.Health;
using Struo.Infrastructure.Localization;
using Struo.Infrastructure.Persistence;
using Struo.Tests.Fixtures;
using Struo.Tests.Support;
using Xunit;
using Xunit.Abstractions;

namespace Struo.Tests.Health;

/// <summary>
/// Stops a throwaway PostgreSQL container under a warm connection pool and checks that the first readiness
/// call afterwards is Unhealthy within the timeout. Runs only when <c>STRUO_TEST_STOP_CONTAINER</c> names a
/// container starting with <c>struo-tr-</c> and <c>STRUO_TEST_PG_CONNECTION</c> points at that container
/// (with <c>Pooling=true</c>, so the pool is warm); otherwise it returns early. It stops the server for
/// every test sharing it, so run it alone: <c>dotnet test --filter DbReadinessStoppedServerLiveTests</c>.
/// </summary>
public sealed class DbReadinessStoppedServerLiveTests(ITestOutputHelper output)
{
    private const string ContainerEnvVar = "STRUO_TEST_STOP_CONTAINER";
    private const string ContainerPrefix = "struo-tr-";
    private const int TimeoutSeconds = 3;

    [Fact]
    public async Task First_readiness_call_after_the_server_stops_is_unhealthy_within_the_timeout()
    {
        var container = Environment.GetEnvironmentVariable(ContainerEnvVar);
        if (string.IsNullOrWhiteSpace(container) || LiveDatabases.Postgres is null) return;
        container.Should().StartWith(ContainerPrefix, "only a throwaway container may be stopped");

        using var database = TestBackend.CreateLiveDatabase(TestBackendKind.PostgreSQL, "readiness");
        var client = SqlSugarClientFactory.Create(
            new DatabaseOptions { DbType = StruoDbType.PostgreSQL, ConnectionString = database.ConnectionString },
            new TestCurrentUserAccessor(Guid.Empty));
        client.CodeFirst.InitTables<Language>();
        var check = new DbReadinessCheck(
            client, Options.Create(new DatabaseOptions { ReadinessTimeoutSeconds = TimeoutSeconds }));

        try
        {
            (await check.CheckHealthAsync(new HealthCheckContext())).Status.Should().Be(HealthStatus.Healthy);

            Docker("stop", container);
            var watch = Stopwatch.StartNew();
            var result = await check.CheckHealthAsync(new HealthCheckContext());
            watch.Stop();
            output.WriteLine($"first readiness call after stop: {result.Status} in {watch.ElapsedMilliseconds} ms");

            result.Status.Should().Be(HealthStatus.Unhealthy);
            watch.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(TimeoutSeconds + 2));
        }
        finally
        {
            Docker("start", container);
            await WaitUntilHealthyAsync(check);
        }
    }

    private static void Docker(string verb, string container)
    {
        using var process = Process.Start(new ProcessStartInfo("docker", [verb, container])
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        })!;
        process.WaitForExit(TimeSpan.FromSeconds(60)).Should().BeTrue();
        process.ExitCode.Should().Be(0, $"docker {verb} {container}");
    }

    private static async Task WaitUntilHealthyAsync(DbReadinessCheck check)
    {
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (DateTime.UtcNow < deadline)
        {
            if ((await check.CheckHealthAsync(new HealthCheckContext())).Status == HealthStatus.Healthy) return;
            await Task.Delay(500);
        }
    }
}
