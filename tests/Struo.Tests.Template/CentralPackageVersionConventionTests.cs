// tests/Struo.Tests.Template/CentralPackageVersionConventionTests.cs
using AwesomeAssertions;
using Struo.Tests.Support;
using Xunit;

namespace Struo.Tests.Template;

/// <summary>
/// Guards central package management: every NuGet version lives in <c>Directory.Packages.props</c>, so
/// no project, props or targets file under <c>src</c>, <c>tests</c> or <c>samples</c> (nor a props or
/// targets file at the repository root) may carry a <c>VersionOverride</c>. An override is not updated
/// when the central pin moves, and it can pin a package below a version another package requires.
/// </summary>
public sealed class CentralPackageVersionConventionTests
{
    private const string Forbidden = "VersionOverride";

    private static readonly string[] ScannedDirectories = ["src", "tests", "samples"];

    private static readonly string[] SkippedSegments = ["bin", "obj", "node_modules", ".worktrees"];

    private static bool IsSkipped(string root, string path) =>
        Path.GetRelativePath(root, path)
            .Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            .Any(segment => SkippedSegments.Contains(segment));

    private static IEnumerable<string> FilesToScan(string root)
    {
        var rootBuildFiles = Directory.EnumerateFiles(root, "*.props").Concat(Directory.EnumerateFiles(root, "*.targets"));

        var nested = ScannedDirectories
            .Select(d => Path.Combine(root, d))
            .Where(Directory.Exists)
            .SelectMany(d => Directory.EnumerateFiles(d, "*.csproj", SearchOption.AllDirectories)
                .Concat(Directory.EnumerateFiles(d, "*.props", SearchOption.AllDirectories))
                .Concat(Directory.EnumerateFiles(d, "*.targets", SearchOption.AllDirectories)));

        return rootBuildFiles.Concat(nested).Where(f => !IsSkipped(root, f)).Distinct();
    }

    [Fact]
    public void No_project_or_build_file_declares_a_VersionOverride()
    {
        var root = RepoRoot.Find();

        var offenders = FilesToScan(root)
            .SelectMany(file => File.ReadAllLines(file)
                .Select((line, i) => (line, number: i + 1))
                .Where(x => x.line.Contains(Forbidden, StringComparison.Ordinal))
                .Select(x => $"{Path.GetRelativePath(root, file).Replace('\\', '/')}:{x.number}: {x.line.Trim()}"))
            .ToList();

        offenders.Should().BeEmpty(
            "NuGet versions are centralized in Directory.Packages.props; remove the override and let the central pin apply");
    }

    [Fact]
    public void The_scan_covers_the_src_tests_and_samples_projects()
    {
        var root = RepoRoot.Find();
        var scanned = FilesToScan(root).Select(f => Path.GetRelativePath(root, f).Replace('\\', '/')).ToList();

        scanned.Should().Contain("Directory.Packages.props");
        scanned.Should().Contain(f => f.StartsWith("src/") && f.EndsWith(".csproj"));
        scanned.Should().Contain(f => f.StartsWith("tests/") && f.EndsWith(".csproj"));
        if (Directory.Exists(Path.Combine(root, "samples")))
            scanned.Should().Contain(f => f.StartsWith("samples/") && f.EndsWith(".csproj"));
        scanned.Should().NotContain(f => f.Contains("/obj/") || f.Contains("/bin/") || f.Contains("node_modules"));
    }
}
