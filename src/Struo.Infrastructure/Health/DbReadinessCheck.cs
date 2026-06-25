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
            var ok = await Task.Run(() => db.Ado.IsValidConnection(), cancellationToken);
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
