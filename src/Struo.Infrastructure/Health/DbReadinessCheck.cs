using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using SqlSugar;
using Struo.Application.Configuration;

namespace Struo.Infrastructure.Health;

/// <summary>
/// Reports the database as ready when a connection can be opened within
/// <see cref="DatabaseOptions.ReadinessTimeoutSeconds"/>. The probe is synchronous, so it runs on a pool
/// thread and is raced against the timeout; a probe that outlives the timeout finishes in the background
/// and its result is discarded.
/// </summary>
public sealed class DbReadinessCheck : IHealthCheck
{
    private readonly ISqlSugarClient _db;
    private readonly Func<bool>? _probe;
    private readonly int _timeoutSeconds;
    private readonly Action<Task<bool>>? _onProbeStarted;

    public DbReadinessCheck(ISqlSugarClient db, IOptions<DatabaseOptions> options)
        : this(db, options, null)
    {
    }

    // onProbeStarted receives the probe task as soon as it is scheduled, so a test can watch its state.
    internal DbReadinessCheck(
        ISqlSugarClient db, IOptions<DatabaseOptions> options, Func<bool>? probe,
        Action<Task<bool>>? onProbeStarted = null)
    {
        _onProbeStarted = onProbeStarted;
        _timeoutSeconds = options.Value.ReadinessTimeoutSeconds;
        _db = db;
        _probe = probe;
    }

    // The copy is made here, not inside the pool-thread lambda: that lambda has no async frame, so
    // SqlSugarScope would park its client on the pool thread for the life of the process. A private
    // copy keeps the singleton out of it; the probe disposes it when it finishes, however late.
    private Func<bool> CreateDatabaseProbe()
    {
        var copy = _db.CopyNew();
        return () =>
        {
            using (copy)
                return copy.Ado.IsValidConnection();
        };
    }

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            var probeTask = Task.Run(_probe ?? CreateDatabaseProbe(), CancellationToken.None);
            _onProbeStarted?.Invoke(probeTask);
            // Reading Exception marks a late failure of an abandoned probe as observed.
            _ = probeTask.ContinueWith(
                static t => _ = t.Exception, TaskContinuationOptions.OnlyOnFaulted);

            using var delayCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            var delayTask = Task.Delay(TimeSpan.FromSeconds(_timeoutSeconds), delayCts.Token);
            var first = await Task.WhenAny(probeTask, delayTask);
            if (first == probeTask)
            {
                await delayCts.CancelAsync();
                return await probeTask
                    ? HealthCheckResult.Healthy()
                    : HealthCheckResult.Unhealthy("Database connection is not valid.");
            }

            cancellationToken.ThrowIfCancellationRequested();
            return HealthCheckResult.Unhealthy(
                $"Database did not answer within the {_timeoutSeconds}-second timeout.");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("Database is unreachable.", ex);
        }
    }
}
