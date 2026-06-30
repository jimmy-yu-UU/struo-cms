using AwesomeAssertions;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using Struo.Infrastructure.Health;
using Xunit;

namespace Struo.Tests.Health;

public class CacheReadinessCheckTests
{
    [Fact]
    public async Task Healthy_when_cache_round_trips()
    {
        IDistributedCache cache = new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions()));
        var check = new CacheReadinessCheck(cache);
        var result = await check.CheckHealthAsync(new HealthCheckContext());
        result.Status.Should().Be(HealthStatus.Healthy);
    }
}
