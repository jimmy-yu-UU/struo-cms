// tests/Struo.Tests.Template/DockerIgnoreConventionTests.cs
using AwesomeAssertions;
using Struo.Tests.Support;
using Xunit;

namespace Struo.Tests.Template;

/// <summary>
/// Guards the two <c>.dockerignore</c> files. A bare pattern such as <c>bin/</c> matches only at the
/// context root, so <c>src/Struo.Api/bin/</c> or <c>src/Struo.Api/App_Data/</c> would still reach the
/// build context. Every pattern therefore has to satisfy one of these rules:
/// <list type="number">
/// <item>it starts with <c>**/</c>, so it matches at every depth;</item>
/// <item>it is one of the known root directories (<c>frontend/</c>, <c>.github/</c>, ...), or names an
/// explicit inner path (a <c>/</c> before its last character) such as <c>src/Struo.Api/bin/</c>;</item>
/// <item>the comment line directly above it contains <c>root-only</c> and says why.</item>
/// </list>
/// Negations (<c>!</c>) are checked on the pattern after the <c>!</c>.
/// </summary>
public sealed class DockerIgnoreConventionTests
{
    public static TheoryData<string> DockerIgnoreFiles => new() { ".dockerignore", "frontend/.dockerignore" };

    private static bool IsExplicitPath(string pattern) => pattern.TrimEnd('/').Contains('/');

    private static bool IsRootOnlyDocumented(string[] lines, int index) =>
        index > 0
        && lines[index - 1].TrimStart().StartsWith('#')
        && lines[index - 1].Contains("root-only", StringComparison.OrdinalIgnoreCase);

    // Directories that exist only at the repository root. A directory pattern has just a trailing
    // slash, so it cannot be told apart from a bare `bin/` without this list.
    private static readonly string[] RootDirectories = [".git/", ".github/", "frontend/", "docs/", ".claude/", ".superpowers/"];

    [Theory]
    [MemberData(nameof(DockerIgnoreFiles))]
    public void Every_pattern_applies_at_every_depth_or_is_explicitly_root_only(string relativePath)
    {
        var lines = File.ReadAllLines(Path.Combine(RepoRoot.Find(), relativePath.Replace('/', Path.DirectorySeparatorChar)));

        var offenders = new List<string>();
        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i].Trim();
            if (line.Length == 0 || line.StartsWith('#')) continue;

            var pattern = line.TrimStart('!');
            var ok = pattern.StartsWith("**/", StringComparison.Ordinal)
                     || RootDirectories.Contains(pattern)
                     || IsExplicitPath(pattern)
                     || IsRootOnlyDocumented(lines, i);
            if (!ok) offenders.Add($"{relativePath}:{i + 1}: {line}");
        }

        offenders.Should().BeEmpty(
            "a bare pattern matches only at the context root; prefix it with `**/` or mark it root-only in the comment above");
    }

    [Fact]
    public void Root_file_excludes_runtime_artifacts_at_every_depth()
    {
        var lines = File.ReadAllLines(Path.Combine(RepoRoot.Find(), ".dockerignore")).Select(l => l.Trim()).ToList();

        lines.Should().Contain(["**/App_Data/", "**/TestResults/", "**/*.log", "**/dist/", "**/.env"]);
    }
}
