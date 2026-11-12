// tests/Struo.Tests/Architecture/ApiItemServiceBoundaryTests.cs
using System.Text.RegularExpressions;
using AwesomeAssertions;
using Xunit;

namespace Struo.Tests.Architecture;

/// <summary>
/// Layering guard: nothing in <c>Struo.Api</c> names the concrete <c>ItemService</c> in code. Controllers
/// and GraphQL resolvers take <c>IItemUseCases</c>, so a replacement registered for that interface
/// serves REST and GraphQL alike. The concrete class is registered in <c>Struo.Infrastructure</c>, which
/// this guard does not scan. A repo-shape check (it reads the Api sources on disk) that ignores
/// comments, so a doc comment may still mention the class.
/// </summary>
public sealed class ApiItemServiceBoundaryTests
{
    private static readonly Regex ConcreteName = new(@"\bItemService\b", RegexOptions.Compiled);
    private static readonly Regex BlockComment = new(@"/\*.*?\*/", RegexOptions.Compiled | RegexOptions.Singleline);

    [Fact]
    public void No_api_type_depends_on_the_concrete_item_service()
    {
        var apiDir = FindApiDir();
        apiDir.Should().NotBeNull("expected to find src/Struo.Api above the test output");

        var files = Directory.GetFiles(apiDir!, "*.cs", SearchOption.AllDirectories)
            .Where(f => !IsBuildOutput(f))
            .ToList();
        files.Should().NotBeEmpty("the guard is worthless if it finds no sources to check");

        var offenders = new List<string>();
        foreach (var file in files)
        {
            var lines = BlockComment.Replace(File.ReadAllText(file), m => new string('\n', m.Value.Count(c => c == '\n')))
                .Split('\n');
            for (var i = 0; i < lines.Length; i++)
            {
                if (ConcreteName.IsMatch(StripLineComment(lines[i])))
                    offenders.Add($"{Path.GetFileName(file)}:{i + 1}");
            }
        }

        offenders.Should().BeEmpty(
            "Struo.Api must depend on IItemUseCases, not on the concrete ItemService, so that a " +
            "replacement registered for IItemUseCases serves REST and GraphQL alike.");
    }

    private static string StripLineComment(string line)
    {
        var at = line.IndexOf("//", StringComparison.Ordinal);
        return at < 0 ? line : line[..at];
    }

    private static bool IsBuildOutput(string path) =>
        path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
        || path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal);

    // Walk up from BaseDirectory to the first ancestor holding src/Struo.Api.
    private static string? FindApiDir()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "src", "Struo.Api");
            if (Directory.Exists(candidate)) return candidate;
        }
        return null;
    }
}
