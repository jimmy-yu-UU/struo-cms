// tests/Struo.Tests.Template/TemplateInvariantsTests.cs
using AwesomeAssertions;
using Struo.Tests.Support;
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
    public void No_test_project_has_a_project_reference_into_samples()
    {
        // The core suite exercises core through tests/Struo.Tests.Fixtures; the sample has its own test
        // project next to it. A reference from any project under tests/ into samples/ would make
        // deleting the sample break it.
        var offenders = ProjectReferences(Path.Combine(RepoRoot.Find(), "tests"))
            .Where(x => ReferencesFolder(x.reference, "samples"))
            .Select(x => $"{x.project}: {x.reference}")
            .ToList();

        offenders.Should().BeEmpty("no project under tests/ may reference samples/*");
    }

    [Fact]
    public void Shipped_src_projects_have_no_project_reference_into_tests()
    {
        // Test fixtures and test support never ship; framework code must not depend on them.
        var offenders = ProjectReferences(Path.Combine(RepoRoot.Find(), "src"))
            .Where(x => ReferencesFolder(x.reference, "tests"))
            .Select(x => $"{x.project}: {x.reference}")
            .ToList();

        offenders.Should().BeEmpty("src/ must not reference tests/* (fixtures and support are non-shipping)");
    }

    [Theory]
    [InlineData(@"..\..\tests\Struo.Tests.Fixtures\Struo.Tests.Fixtures.csproj", "tests", true)]
    [InlineData("../../tests/Struo.Tests.Fixtures/Struo.Tests.Fixtures.csproj", "tests", true)]
    [InlineData(@"..\..\samples\Struo.Sample.Blog\Struo.Sample.Blog.csproj", "samples", true)]
    [InlineData("../../samples/Struo.Sample.Blog/Struo.Sample.Blog.csproj", "samples", true)]
    [InlineData(@"..\Struo.Domain\Struo.Domain.csproj", "tests", false)]
    [InlineData("../Struo.Application/Struo.Application.csproj", "samples", false)]
    [InlineData(@"..\Struo.Tests.Support\Struo.Tests.Support.csproj", "tests", false)]
    public void ReferencesFolder_matches_either_separator_on_any_os(string reference, string folder, bool expected) =>
        ReferencesFolder(reference, folder).Should().Be(expected);

    // Both csproj separators, so the checks do not depend on the OS running the test.
    private static readonly char[] Separators = [(char)92, (char)47];

    // True when a path segment of the reference is exactly the folder name, whichever separator the
    // csproj uses and whichever OS runs the test.
    private static bool ReferencesFolder(string reference, string folder) =>
        reference.Split(Separators).Any(segment => segment.Equals(folder, StringComparison.OrdinalIgnoreCase));

    private static IEnumerable<(string project, string reference)> ProjectReferences(string directory) =>
        Directory.EnumerateFiles(directory, "*.csproj", SearchOption.AllDirectories)
            .Where(path => !path.Split(Separators).Contains("obj"))
            .SelectMany(path => System.Xml.Linq.XDocument.Load(path)
                .Descendants("ProjectReference")
                .Select(e => e.Attribute("Include")?.Value)
                .OfType<string>()
                .Select(include => (Path.GetFileName(path), include)));

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
