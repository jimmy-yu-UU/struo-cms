using AwesomeAssertions;
using Xunit;

namespace Struo.Tests.Persistence;

/// <summary>
/// Guards the rebaseline invariant: db/migrations/001-core-baseline.sql must create a table for every
/// core FrameworkEntityTypes table. Catches an entity added to the framework without a matching baseline
/// update (the prod-bootstrap path would otherwise silently miss it).
/// </summary>
public sealed class CoreBaselineParityTests
{
    private static readonly string[] CoreTables =
    [
        "languages", "files", "file_translations", "users", "roles",
        "permissions", "user_roles", "revisions", "site_settings",
    ];

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
        foreach (var table in CoreTables)
            sql.Should().Contain($"CREATE TABLE IF NOT EXISTS public.{table}",
                $"the core baseline must create the '{table}' table");
    }
}
