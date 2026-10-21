using Microsoft.Extensions.Diagnostics.HealthChecks;
using SqlSugar;

namespace Struo.Infrastructure.Health;

public sealed class DbReadinessCheck(ISqlSugarClient db) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            // The lambda has no async frame, so SqlSugarScope would park its client on the pool thread
            // for the life of the process; a private copy keeps the singleton out of it.
            using var probe = db.CopyNew();
            var ok = await Task.Run(() => probe.Ado.IsValidConnection(), cancellationToken);
            return ok
                ? HealthCheckResult.Healthy()
                : HealthCheckResult.Unhealthy("Database connection is not valid.");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("Database is unreachable.", ex);
        }
    }
}
