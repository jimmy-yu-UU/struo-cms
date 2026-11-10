// tests/Struo.Tests.Template/DependabotToolsConventionTests.cs
using System.Text.Json;
using AwesomeAssertions;
using Struo.Tests.Support;
using Xunit;

namespace Struo.Tests.Template;

/// <summary>
/// Guards how Dependabot treats the tools in <c>.config/dotnet-tools.json</c>. They run only in the
/// <c>sonar-backend</c> job, which does not run on a Dependabot pull request, so no check on the pull
/// request exercises them. Each tool therefore sits in its own <c>dotnet-tools</c> group, listed before
/// <c>backend-minor-patch</c> (Dependabot assigns a dependency to the first group whose patterns match),
/// and <c>dependabot-auto-merge.yml</c> leaves any pull request that touches one for manual review.
/// </summary>
public sealed class DependabotToolsConventionTests
{
    private const string NugetEcosystemHeader = "- package-ecosystem: nuget";
    private const string EcosystemPrefix = "- package-ecosystem:";

    private static string Read(params string[] relativePath) =>
        File.ReadAllText(Path.Combine([RepoRoot.Find(), .. relativePath]));

    private static string[] ToolNames()
    {
        using var doc = JsonDocument.Parse(Read(".config", "dotnet-tools.json"));
        return doc.RootElement.GetProperty("tools").EnumerateObject().Select(p => p.Name).ToArray();
    }

    private static string[] NugetBlockLines()
    {
        var lines = Read(".github", "dependabot.yml").Split('\n').Select(l => l.TrimEnd('\r')).ToArray();
        var start = Array.FindIndex(lines, l => l.Trim() == NugetEcosystemHeader);
        start.Should().BeGreaterThanOrEqualTo(0, "dependabot.yml has a nuget entry");
        return lines.Skip(start + 1).TakeWhile(l => !l.Trim().StartsWith(EcosystemPrefix, StringComparison.Ordinal)).ToArray();
    }

    private static string[] StepLines(string[] workflowLines, string stepName)
    {
        var start = Array.FindIndex(workflowLines, l => l.Trim() == $"- name: {stepName}");
        start.Should().BeGreaterThanOrEqualTo(0, $"the workflow has a step named '{stepName}'");
        return workflowLines.Skip(start + 1).TakeWhile(l => !l.TrimStart().StartsWith("- name:", StringComparison.Ordinal)).ToArray();
    }

    [Fact]
    public void The_tool_manifest_lists_at_least_one_tool()
    {
        ToolNames().Should().NotBeEmpty();
    }

    [Fact]
    public void Every_dotnet_tool_is_a_pattern_of_the_dotnet_tools_group()
    {
        var block = NugetBlockLines();
        var header = Array.FindIndex(block, l => l.Trim() == "dotnet-tools:");
        header.Should().BeGreaterThanOrEqualTo(0, "the nuget entry has a dotnet-tools group");

        var patterns = block.Skip(header + 1).First(l => l.Trim().StartsWith("patterns:", StringComparison.Ordinal));

        ToolNames().Where(tool => !patterns.Contains($"\"{tool}\"", StringComparison.Ordinal)).Should().BeEmpty(
            "every tool in .config/dotnet-tools.json must be in the dotnet-tools group's patterns");
    }

    [Fact]
    public void The_dotnet_tools_group_comes_before_backend_minor_patch()
    {
        var block = NugetBlockLines();
        var tools = Array.FindIndex(block, l => l.Trim() == "dotnet-tools:");
        var backend = Array.FindIndex(block, l => l.Trim() == "backend-minor-patch:");

        tools.Should().BeGreaterThanOrEqualTo(0);
        backend.Should().BeGreaterThanOrEqualTo(0);
        tools.Should().BeLessThan(backend,
            "Dependabot assigns a dependency to the first group whose patterns match, and backend-minor-patch matches everything");
    }

    [Theory]
    [InlineData("Enable auto-merge")]
    [InlineData("Explain why this pull request was left for review")]
    public void Both_auto_merge_steps_test_every_dotnet_tool(string stepName)
    {
        var step = string.Join('\n', StepLines(Read(".github", "workflows", "dependabot-auto-merge.yml").Split('\n'), stepName));

        ToolNames().Where(tool => !step.Contains($"contains(steps.metadata.outputs.dependency-names, '{tool}')", StringComparison.Ordinal))
            .Should().BeEmpty($"the '{stepName}' step must exclude pull requests that touch a dotnet tool");
    }
}
