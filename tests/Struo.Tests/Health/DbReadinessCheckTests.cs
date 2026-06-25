using FluentAssertions;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Struo.Application.Configuration;
using Struo.Infrastructure.Health;
using Struo.Infrastructure.Persistence;
using Struo.Sample.Blog;
using Struo.Tests.Support;
using Xunit;

namespace Struo.Tests.Health;

public class DbReadinessCheckTests
{
    [Fact]
    public async Task Reports_healthy_when_database_reachable()
    {
        using var db = new SqliteTestDatabase();
        var client = SqlSugarClientFactory.Create(
            new DatabaseOptions { DbType = StruoDbType.Sqlite, ConnectionString = db.ConnectionString },
            new TestCurrentUserAccessor("system"));
        client.CodeFirst.InitTables<Article>();

        var check = new DbReadinessCheck(client);
        var result = await check.CheckHealthAsync(new HealthCheckContext());

        result.Status.Should().Be(HealthStatus.Healthy);
    }

    [Fact]
    public async Task Reports_unhealthy_when_database_unreachable()
    {
        var client = SqlSugarClientFactory.Create(
            new DatabaseOptions
            {
                DbType = StruoDbType.Sqlite,
                ConnectionString = "Data Source=/nonexistent-dir/struo_missing.db;Mode=ReadOnly"
            },
            new TestCurrentUserAccessor("system"));

        var check = new DbReadinessCheck(client);
        var result = await check.CheckHealthAsync(new HealthCheckContext());

        result.Status.Should().Be(HealthStatus.Unhealthy);
    }
}
