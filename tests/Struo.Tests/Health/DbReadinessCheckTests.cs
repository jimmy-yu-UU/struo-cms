using AwesomeAssertions;
using Microsoft.Extensions.Diagnostics.HealthChecks;
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

        var check = new DbReadinessCheck(client);
        var result = await check.CheckHealthAsync(new HealthCheckContext());

        result.Status.Should().Be(HealthStatus.Healthy);
    }

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

        var check = new DbReadinessCheck(client);
        var result = await check.CheckHealthAsync(new HealthCheckContext());

        result.Status.Should().Be(HealthStatus.Unhealthy);
    }
}
