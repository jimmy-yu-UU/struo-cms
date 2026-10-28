// tests/Struo.Tests.Template/CiWorkflowConventionTests.cs
using System.Text.RegularExpressions;
using AwesomeAssertions;
using Struo.Tests.Support;
using Xunit;

namespace Struo.Tests.Template;

/// <summary>
/// Guards <c>.github/workflows/ci.yml</c>: every job bounds its run time, and no step installs a
/// tool or action at an unpinned version. Tools come from <c>.config/dotnet-tools.json</c> (restored with
/// <c>dotnet tool restore</c>) and from the frontend lockfile, so a new upstream release never
/// reaches CI unreviewed.
/// </summary>
public sealed partial class CiWorkflowConventionTests
{
    [GeneratedRegex(@"^  ([A-Za-z0-9_-]+):\s*$")]
    private static partial Regex JobHeader();

    [GeneratedRegex(@"^    timeout-minutes:\s*\d+\s*$")]
    private static partial Regex JobTimeout();

    [GeneratedRegex(@"dotnet\s+tool\s+install\s+(--global|-g)\b|\bpnpm\s+dlx\b")]
    private static partial Regex UnpinnedToolInstall();

    [GeneratedRegex(@"^\s*-?\s*uses:\s*(\S+)")]
    private static partial Regex UsesLine();

    [GeneratedRegex(@"@[0-9a-f]{40}$")]
    private static partial Regex FullShaPin();

    private static bool IsLocalOrDocker(string reference) =>
        reference.StartsWith("./", StringComparison.Ordinal) || reference.StartsWith("docker://", StringComparison.Ordinal);

    private static string[] WorkflowLines() =>
        File.ReadAllLines(Path.Combine(RepoRoot.Find(), ".github", "workflows", "ci.yml"));

    [Fact]
    public void Every_job_sets_timeout_minutes()
    {
        var lines = WorkflowLines();
        var start = Array.FindIndex(lines, l => l.TrimEnd() == "jobs:");
        start.Should().BeGreaterThanOrEqualTo(0, "ci.yml has a jobs: section");

        var jobs = new List<(string name, bool hasTimeout)>();
        foreach (var line in lines.Skip(start + 1))
        {
            var header = JobHeader().Match(line);
            if (header.Success) jobs.Add((header.Groups[1].Value, false));
            else if (jobs.Count > 0 && JobTimeout().IsMatch(line)) jobs[^1] = (jobs[^1].name, true);
        }

        jobs.Should().NotBeEmpty();
        jobs.Where(j => !j.hasTimeout).Select(j => j.name).Should().BeEmpty(
            "every job needs a job-level timeout-minutes so a hung step cannot hold a runner for six hours");
    }

    [Fact]
    public void No_step_installs_an_unpinned_tool()
    {
        var offenders = WorkflowLines()
            .Select((line, i) => (line, number: i + 1))
            .Where(x => !x.line.TrimStart().StartsWith('#') && UnpinnedToolInstall().IsMatch(x.line))
            .Select(x => $"ci.yml:{x.number}: {x.line.Trim()}")
            .ToList();

        offenders.Should().BeEmpty(
            "install .NET tools with `dotnet tool restore` from .config/dotnet-tools.json and use a pinned action or a locked dev dependency for anything else");
    }

    [Theory]
    [InlineData("struo-api:ci")]
    [InlineData("struo-admin:ci")]
    public void Docker_job_fails_on_a_fixable_high_or_critical_finding_in_each_image(string image)
    {
        var lines = WorkflowLines();
        var start = Array.FindIndex(lines, l => l.TrimEnd() == "  docker:");
        start.Should().BeGreaterThanOrEqualTo(0, "ci.yml has a docker job");
        var jobLines = lines.Skip(start + 1).TakeWhile(l => !JobHeader().IsMatch(l));

        var steps = new List<List<string>>();
        foreach (var line in jobLines)
        {
            if (line.StartsWith("      - ", StringComparison.Ordinal)) steps.Add([]);
            steps.LastOrDefault()?.Add(line.Trim());
        }

        var gate = steps.Where(s => s.Any(l => l.Contains("aquasecurity/trivy-action@") && l.Contains("uses:")))
            .Where(s => s.Any(l => l == $"image-ref: {image}"))
            .ToList();

        gate.Should().Contain(
            s => s.Contains("exit-code: '1'") && s.Contains("ignore-unfixed: true") && s.Contains("severity: HIGH,CRITICAL"),
            "a gating Trivy scan must fail the job on fixable HIGH/CRITICAL findings, ignoring ones with no upstream fix");
    }

    [Fact]
    public void Every_action_is_pinned_to_a_full_commit_sha()
    {
        var offenders = WorkflowLines()
            .Select((line, i) => (match: UsesLine().Match(line), number: i + 1))
            .Where(x => x.match.Success && !IsLocalOrDocker(x.match.Groups[1].Value) && !FullShaPin().IsMatch(x.match.Groups[1].Value))
            .Select(x => $"ci.yml:{x.number}: {x.match.Groups[1].Value}")
            .ToList();

        offenders.Should().BeEmpty(
            "a tag can be moved after review; Dependabot's github-actions entry updates SHA pins");
    }
}
