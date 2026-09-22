using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Struo.Infrastructure.Health;

public sealed class CacheReadinessCheck(IDistributedCache cache) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            const string key = "health:ping";
            await cache.SetStringAsync(key, "1", new DistributedCacheEntryOptions
            { AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(5) }, cancellationToken);
            var v = await cache.GetStringAsync(key, cancellationToken);
            return v == "1" ? HealthCheckResult.Healthy() : HealthCheckResult.Unhealthy("cache round-trip mismatch");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("cache unavailable", ex);
        }
    }
}
