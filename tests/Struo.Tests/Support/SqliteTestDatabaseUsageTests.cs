using System.Text.RegularExpressions;
using AwesomeAssertions;
using Xunit;

namespace Struo.Tests.Support;

/// <summary>
/// Tests outside <c>Support/</c> get their database from <see cref="TestBackend"/>, so they run on the
/// backend <c>STRUO_TEST_BACKEND</c> selects. Using <see cref="SqliteTestDatabase"/> or
/// <see cref="SqliteTempTestDatabase"/> directly pins a test to SQLite; a SQLite-only test says so with
/// <see cref="SqliteOnlyFactAttribute"/> instead.
/// </summary>
public sealed partial class SqliteTestDatabaseUsageTests
{
    [GeneratedRegex(@"\bSqlite(Temp)?TestDatabase\b")]
    private static partial Regex DirectUse();

    [Fact]
    public void No_test_outside_Support_uses_SqliteTestDatabase_directly()
    {
        var root = Path.Combine(RepoRoot.Find(), "tests", "Struo.Tests");
        var offenders = Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories)
            .Select(path => Path.GetRelativePath(root, path).Replace('\\', '/'))
            .Where(relative => !relative.StartsWith("Support/", StringComparison.Ordinal)
                               && !relative.StartsWith("bin/", StringComparison.Ordinal)
                               && !relative.StartsWith("obj/", StringComparison.Ordinal))
            .SelectMany(relative => File.ReadLines(Path.Combine(root, relative))
                .Select((line, index) => (relative, line, number: index + 1)))
            .Where(x => !x.line.TrimStart().StartsWith("//", StringComparison.Ordinal) && DirectUse().IsMatch(x.line))
            .Select(x => $"{x.relative}:{x.number}")
            .ToList();

        offenders.Should().BeEmpty("use TestBackend.CreateUnitDatabase() so the test follows STRUO_TEST_BACKEND");
    }
}
