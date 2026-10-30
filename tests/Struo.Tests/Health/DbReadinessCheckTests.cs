using System.ComponentModel.DataAnnotations;
using System.Diagnostics;
using AwesomeAssertions;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using Struo.Application.Configuration;
using Struo.Infrastructure.Health;
using Struo.Infrastructure.Persistence;
using Struo.Tests.Fixtures;
using Struo.Tests.Support;
using Xunit;

namespace Struo.Tests.Health;

public class DbReadinessCheckTests
{
    [Fact]
    public async Task Reports_healthy_when_database_reachable()
    {
        using var db = TestBackend.CreateUnitDatabase();
        var client = SqlSugarClientFactory.Create(
            db.Options(),
            new TestCurrentUserAccessor(Guid.Empty));
        client.CodeFirst.InitTables<FxArticle>();

        var check = new DbReadinessCheck(client, Options.Create(new DatabaseOptions()));
        var result = await check.CheckHealthAsync(new HealthCheckContext());

        result.Status.Should().Be(HealthStatus.Healthy);
    }

    [Fact]
    public async Task Reports_unhealthy_within_the_timeout_when_the_probe_blocks()
    {
        using var release = new ManualResetEventSlim();
        var check = new DbReadinessCheck(
            null!, Options.Create(new DatabaseOptions { ReadinessTimeoutSeconds = 1 }),
            () => { release.Wait(); return true; });

        var watch = Stopwatch.StartNew();
        var result = await check.CheckHealthAsync(new HealthCheckContext());
        watch.Stop();
        release.Set();

        result.Status.Should().Be(HealthStatus.Unhealthy);
        result.Description.Should().Be("Database did not answer within the 1-second timeout.");
        watch.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task Reports_healthy_when_a_probe_answers_inside_the_timeout()
    {
        var check = new DbReadinessCheck(
            null!, Options.Create(new DatabaseOptions { ReadinessTimeoutSeconds = 1 }), () => true);

        var result = await check.CheckHealthAsync(new HealthCheckContext());

        result.Status.Should().Be(HealthStatus.Healthy);
    }

    private sealed class LateProbeFailure() : Exception("late failure");

    // Polls IsFaulted, which does not observe the exception, then drops the only test-side reference so the
    // faulted task is collectable. Kept out of line so no local keeps the task alive in the caller.
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private static bool WaitUntilFaultedThenDrop(ref Task<bool>? probeTask)
    {
        var task = probeTask!;
        var faulted = SpinWait.SpinUntil(() => task.IsFaulted, TimeSpan.FromSeconds(10));
        task = null!;
        probeTask = null;
        return faulted;
    }

    [Fact]
    public async Task A_probe_that_fails_after_the_timeout_raises_no_unobserved_exception()
    {
        var unobserved = new System.Collections.Concurrent.ConcurrentQueue<Exception>();
        void OnUnobserved(object? s, UnobservedTaskExceptionEventArgs e)
        {
            if (e.Exception.Flatten().InnerExceptions.OfType<LateProbeFailure>().Any())
            {
                unobserved.Enqueue(e.Exception);
            }
        }

        TaskScheduler.UnobservedTaskException += OnUnobserved;
        try
        {
            using var release = new ManualResetEventSlim();
            Task<bool>? probeTask = null;
            var check = new DbReadinessCheck(
                null!, Options.Create(new DatabaseOptions { ReadinessTimeoutSeconds = 1 }),
                () =>
                {
                    release.Wait();
                    throw new LateProbeFailure();
                },
                task => probeTask = task);

            var result = await check.CheckHealthAsync(new HealthCheckContext());
            release.Set();
            WaitUntilFaultedThenDrop(ref probeTask).Should().BeTrue("the probe task must be Faulted before the GC");
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            GC.WaitForPendingFinalizers();

            result.Status.Should().Be(HealthStatus.Unhealthy);
            unobserved.Should().BeEmpty();
        }
        finally
        {
            TaskScheduler.UnobservedTaskException -= OnUnobserved;
        }
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(-1, false)]
    [InlineData(61, false)]
    [InlineData(1, true)]
    [InlineData(3, true)]
    [InlineData(60, true)]
    public void ReadinessTimeoutSeconds_is_validated(int seconds, bool valid)
    {
        var options = new DatabaseOptions { ConnectionString = "x", ReadinessTimeoutSeconds = seconds };

        Validator.TryValidateObject(options, new ValidationContext(options), null, true).Should().Be(valid);
    }

    [Fact]
    public void ReadinessTimeoutSeconds_defaults_to_three()
        => new DatabaseOptions().ReadinessTimeoutSeconds.Should().Be(3);

    [SqliteOnlyFact("unreachable database simulated by a read-only SQLite path in a missing directory")]
    public async Task Reports_unhealthy_when_database_unreachable()
    {
        var client = SqlSugarClientFactory.Create(
            new DatabaseOptions
            {
                DbType = StruoDbType.Sqlite,
                ConnectionString = "Data Source=/nonexistent-dir/struo_missing.db;Mode=ReadOnly"
            },
            new TestCurrentUserAccessor(Guid.Empty));

        var check = new DbReadinessCheck(client, Options.Create(new DatabaseOptions()));
        var result = await check.CheckHealthAsync(new HealthCheckContext());

        result.Status.Should().Be(HealthStatus.Unhealthy);
    }
}
