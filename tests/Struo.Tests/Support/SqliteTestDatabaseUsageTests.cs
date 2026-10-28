using System.Text.RegularExpressions;
using AwesomeAssertions;
using Xunit;

namespace Struo.Tests.Support;

/// <summary>
/// Tests in every test project (<c>tests/*</c> except the Support library, and <c>samples/*Tests</c>) get
/// their database from <see cref="TestBackend"/>, so they run on the
/// backend <c>STRUO_TEST_BACKEND</c> selects. Using <see cref="SqliteTestDatabase"/> or
/// <see cref="SqliteTempTestDatabase"/> directly pins a test to SQLite; a SQLite-only test says so with
/// <see cref="SqliteOnlyFactAttribute"/> instead. <c>TestBackend.CreateSqliteDatabase()</c> pins the same
/// way, so only the files in <see cref="SqliteCreators"/> may call it.
/// </summary>
public sealed partial class SqliteTestDatabaseUsageTests
{
    [GeneratedRegex(@"\bSqlite(Temp)?TestDatabase\b")]
    private static partial Regex DirectUse();

    [GeneratedRegex(@"\bCreateSqliteDatabase\b")]
    private static partial Regex SqliteFactoryUse();

    // Files that call TestBackend.CreateSqliteDatabase() on purpose, with the reason.
    private static readonly Dictionary<string, string> SqliteCreators = new()
    {
        ["Struo.Tests/DependencyInjection/OptionsValidationTests.cs"] = "[SqliteOnly] cases; the file only satisfies the required connection string",
        ["Struo.Tests/Migrations/CrossProviderStartupGateLiveTests.cs"] = "the SQLite side of the cross-provider gate is SQLite by definition",
        ["Struo.Tests/Migrations/Schema/IndexCatalogTests.cs"] = "[SqliteOnly] Sqlite_* catalog tests",
        ["Struo.Tests/Migrations/StruoMigrationTypeTests.cs"] = "[SqliteOnly] SQLite literal test",
        ["Struo.Tests/Persistence/SqlServerIsolationCheckTests.cs"] = "the non-SQL Server cases only need a client that counts statements",
    };

    [Fact]
    public void No_test_outside_Support_uses_SqliteTestDatabase_directly()
    {
        var offenders = TestSources()
            .Where(x => !IsComment(x.line) && DirectUse().IsMatch(x.line))
            .Select(x => $"{x.relative}:{x.number}")
            .ToList();

        offenders.Should().BeEmpty("use TestBackend.CreateUnitDatabase() so the test follows STRUO_TEST_BACKEND");
    }

    [Fact]
    public void Only_allow_listed_files_call_CreateSqliteDatabase()
    {
        var offenders = TestSources()
            .Where(x => !IsComment(x.line) && SqliteFactoryUse().IsMatch(x.line) && !SqliteCreators.ContainsKey(x.relative))
            .Select(x => $"{x.relative}:{x.number}")
            .ToList();

        offenders.Should().BeEmpty(
            "use TestBackend.CreateUnitDatabase(), or add the file to SqliteCreators with the reason it needs SQLite");
    }

    private static bool IsComment(string line) => line.TrimStart().StartsWith("//", StringComparison.Ordinal);

    private static IEnumerable<(string relative, string line, int number)> TestSources()
    {
        var repo = RepoRoot.Find();
        var roots = new[] { Path.Combine(repo, "tests"), Path.Combine(repo, "samples") }
            .Where(Directory.Exists)
            .SelectMany(Directory.EnumerateDirectories)
            .Where(IsTestProject)
            .ToList();

        return roots.SelectMany(projectDir =>
        {
            var root = Path.GetDirectoryName(projectDir)!;
            return Directory.EnumerateFiles(projectDir, "*.cs", SearchOption.AllDirectories)
                .Select(path => Path.GetRelativePath(root, path).Replace('\\', '/'))
                .Where(relative => !IsProjectSupport(relative) && !IsBuildOutput(relative))
                .SelectMany(relative => File.ReadLines(Path.Combine(root, relative))
                    .Select((line, index) => (relative, line, number: index + 1)));
        });
    }

    // The Support library is where SqliteTestDatabase lives; every other test project is scanned.
    private static bool IsTestProject(string dir)
    {
        var name = Path.GetFileName(dir);
        return name != "Struo.Tests.Support"
               && name.StartsWith("Struo", StringComparison.Ordinal)
               && name.Contains("Tests", StringComparison.Ordinal);
    }

    // relative is "<project>/<path>"; only the project's own top-level Support folder is excluded.
    private static bool IsProjectSupport(string relative) =>
        relative.Split('/') is [_, "Support", ..];

    private static bool IsBuildOutput(string relative) =>
        relative.Contains("/bin/", StringComparison.Ordinal) || relative.Contains("/obj/", StringComparison.Ordinal);
}
