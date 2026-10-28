// tests/Struo.Tests.Template/TemplateInvariantsTests.cs
using AwesomeAssertions;
using Xunit;

namespace Struo.Tests.Template;

/// <summary>
/// Guards two template-level invariants: the dependency rule ("framework code never references
/// samples/*") and the core/sample boundary (the shipped template ships with no business content
/// wired in). These are repo-shape checks — they inspect files on disk rather than exercising
/// <c>AddStruoMetadata</c> — so they live here rather than alongside the metadata-scanning tests.
/// </summary>
public sealed class TemplateInvariantsTests
{
    [Fact]
    public void Shipped_appsettings_declares_no_content_assemblies()
    {
        // The template ships without sample content wired in: Struo:ContentAssemblies is empty and the
        // host project has no reference to samples/. Opting the sample in is a documented downstream step.
        var appsettingsPath = Path.Combine(
            AppContext.BaseDirectory, "appsettings.json");
        var json = File.ReadAllText(appsettingsPath);
        using var doc = System.Text.Json.JsonDocument.Parse(json);

        var contentAssemblies = doc.RootElement
            .GetProperty("Struo").GetProperty("ContentAssemblies")
            .EnumerateArray().Select(e => e.GetString()).ToArray();

        contentAssemblies.Should().BeEmpty();
    }

    [Fact]
    public void Host_project_has_no_project_reference_into_samples()
    {
        // Dependency rule: framework code never references samples/*. The
        // appsettings check above only proves the sample isn't *opted in* via config - it says
        // nothing about the project file. Re-adding a <ProjectReference> to samples/ would restore
        // the dependency-rule violation the whole sample-decoupling task exists to remove, while
        // leaving every config-only check green. This test inspects the .csproj directly so that
        // regression can't slip through.
        var apiDir = FindApiDir();
        apiDir.Should().NotBeNull("the Struo.Api project directory must be locatable from the test output directory");
        var csprojPath = Path.Combine(apiDir!, "Struo.Api.csproj");
        File.Exists(csprojPath).Should().BeTrue($"expected to find {csprojPath}");

        var csproj = File.ReadAllText(csprojPath);
        var referencesSamples = csproj.Contains("ProjectReference", StringComparison.Ordinal)
            && csproj.Split('\n')
                .Where(line => line.Contains("ProjectReference", StringComparison.Ordinal))
                .Any(line => line.Contains("samples", StringComparison.OrdinalIgnoreCase));

        referencesSamples.Should().BeFalse(
            "Struo.Api must not reference samples/* (the dependency rule: framework code " +
            "never references samples/*; the sample is a demo, deleted on fork, and pulling it back " +
            "in via a ProjectReference would re-couple the shipped framework to business content).");
    }

    [Fact]
    public void Core_test_project_has_no_project_reference_into_samples()
    {
        // Struo.Tests exercises core through tests/Struo.Tests.Fixtures; the sample has its own test
        // project. A reference from the core suite into samples/ would make deleting the sample break it.
        var offenders = ProjectReferenceLines(Path.Combine(RepoRoot(), "tests", "Struo.Tests"))
            .Where(x => x.reference.Contains("samples", StringComparison.OrdinalIgnoreCase))
            .Select(x => $"{x.project}: {x.reference}")
            .ToList();

        offenders.Should().BeEmpty("the core test project must not reference samples/*");
    }

    [Fact]
    public void Shipped_src_projects_have_no_project_reference_into_tests()
    {
        // Test fixtures and test support never ship; framework code must not depend on them.
        var offenders = ProjectReferenceLines(Path.Combine(RepoRoot(), "src"))
            .Where(x => x.reference.Replace(Path.DirectorySeparatorChar, (char)47).Contains("tests/", StringComparison.OrdinalIgnoreCase))
            .Select(x => $"{x.project}: {x.reference}")
            .ToList();

        offenders.Should().BeEmpty("src/ must not reference tests/* (fixtures and support are non-shipping)");
    }

    private static IEnumerable<(string project, string reference)> ProjectReferenceLines(string directory) =>
        Directory.EnumerateFiles(directory, "*.csproj", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .SelectMany(path => File.ReadLines(path)
                .Where(line => line.Contains("ProjectReference", StringComparison.Ordinal))
                .Select(line => (Path.GetFileName(path), line.Trim())));

    private static string RepoRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
            if (Directory.Exists(Path.Combine(dir.FullName, "src", "Struo.Api"))) return dir.FullName;
        throw new DirectoryNotFoundException("the repository root is not an ancestor of the test output directory");
    }

    // Same walk-up-from-BaseDirectory approach as LiveDatabases.FindApiDir - see
    // tests/Struo.Tests.Support/LiveDatabases.cs.
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
