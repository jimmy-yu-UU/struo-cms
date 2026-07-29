using AwesomeAssertions;
using SqlSugar;
using Struo.Application.Configuration;
using Struo.Infrastructure.Metadata;
using Struo.Infrastructure.Persistence;
using Struo.Tests.Support;
using Xunit;

namespace Struo.Tests.Persistence;

/// <summary>
/// Guards the rebaseline invariant: db/migrations/001-core-baseline.sql must create a table for every
/// core FrameworkEntityTypes table. Catches an entity added to the framework without a matching baseline
/// update (the prod-bootstrap path would otherwise silently miss it) — this is exactly what happened when
/// MediaFolder was added while this test still hardcoded a 9-table list. Expected table names are now
/// derived from FrameworkEntityTypes.All by resolving each CLR type to its physical table name the same
/// way SqlSugar does (ISqlSugarClient.EntityMaintenance.GetTableName), so the list can never itself go
/// stale again.
/// </summary>
public sealed class CoreBaselineParityTests
{
    private static IReadOnlyList<string> ExpectedCoreTables()
    {
        var db = new SqliteTestDatabase();
        using (db)
        {
            var client = SqlSugarClientFactory.Create(
                new DatabaseOptions { DbType = StruoDbType.Sqlite, ConnectionString = db.ConnectionString },
                new TestCurrentUserAccessor(Guid.Empty));
            return FrameworkEntityTypes.All
                .Select(client.EntityMaintenance.GetTableName)
                .ToList();
        }
    }

    private static string BaselinePath()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "db", "migrations")))
            dir = dir.Parent;
        dir.Should().NotBeNull("the repo's db/migrations directory must be locatable from the test host");
        return Path.Combine(dir!.FullName, "db", "migrations", "001-core-baseline.sql");
    }

    [Fact]
    public void Baseline_creates_every_core_table()
    {
        var sql = File.ReadAllText(BaselinePath());
        foreach (var table in ExpectedCoreTables())
            sql.Should().Contain($"CREATE TABLE IF NOT EXISTS public.{table}",
                $"the core baseline must create the '{table}' table");
    }
}
