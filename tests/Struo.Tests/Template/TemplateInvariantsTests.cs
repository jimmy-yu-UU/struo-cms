// tests/Struo.Tests/Template/TemplateInvariantsTests.cs
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
            "Struo.Api must not reference samples/* (CLAUDE.md §2 dependency rule: framework code " +
            "never references samples/*; the sample is a demo, deleted on fork, and pulling it back " +
            "in via a ProjectReference would re-couple the shipped framework to business content).");
    }

    // Same walk-up-from-BaseDirectory approach as PostgresIntegrationTests.FindApiDir - see
    // tests/Struo.Tests/Query/PostgresIntegrationTests.cs.
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
