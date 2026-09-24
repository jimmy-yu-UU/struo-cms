using AwesomeAssertions;
using Struo.Tests.Support;

namespace Struo.Tests.Documentation;

/// <summary>
/// Repo-wide guard for the "current state only" convention: comments and prose must carry no change
/// narrative, bare date, or PR/issue reference, and every decision file under
/// <c>docs/ai/decisions</c> must have the required heading structure and be referenced from at least
/// one class doc or agent document. Rule text lives in <c>docs/ai/conventions.md</c>.
/// </summary>
public sealed class StaleNarrativeConventionTests
{
    private static readonly string[] ScanRootFiles = ["AGENTS.md", "CLAUDE.md"];
    private static readonly string[] ScanRootDirs =
        ["docs/ai", "docs/guide", "src", "tests", "frontend/src", "frontend/e2e"];
    private const string DecisionsPrefix = "docs/ai/decisions/";

    [Fact]
    public void No_change_narrative_bare_dates_or_pr_references_in_comments_and_prose()
    {
        var repoRoot = RepoRoot.Find();
        var scanFiles = RepositoryFiles.Enumerate(
            repoRoot, ScanRootFiles, ScanRootDirs, NarrativeScanScope.Includes);
        var violations = new List<StaleNarrativeRules.Violation>();
        foreach (var relativePath in scanFiles)
        {
            var lines = File.ReadAllLines(Path.Combine(repoRoot, relativePath));
            var kind = StaleNarrativeRules.KindOf(relativePath);
            var scannable = StaleNarrativeRules.ExtractScannable(kind, lines);
            var isDecisionFile = relativePath.StartsWith(DecisionsPrefix, StringComparison.Ordinal);
            violations.AddRange(StaleNarrativeRules.Check(relativePath, scannable, isDecisionFile));
        }

        violations.Should().BeEmpty(BuildFailureMessage(violations));
    }

    [Fact]
    public void Every_decision_file_is_well_formed_and_referenced()
    {
        var repoRoot = RepoRoot.Find();
        var decisionsDir = Path.Combine(repoRoot, "docs", "ai", "decisions");
        var decisionFiles = Directory.Exists(decisionsDir)
            ? Directory.GetFiles(decisionsDir, "*.md")
                .Select(f => Path.GetRelativePath(repoRoot, f).Replace('\\', '/'))
                .OrderBy(f => f, StringComparer.Ordinal)
                .ToList()
            : new List<string>();

        var violations = new List<StaleNarrativeRules.Violation>();
        if (decisionFiles.Count > 0)
        {
            var referenceCorpus = BuildReferenceCorpus(repoRoot);
            foreach (var relativePath in decisionFiles)
            {
                var lines = File.ReadAllLines(Path.Combine(repoRoot, relativePath));
                violations.AddRange(StaleNarrativeRules.CheckDecisionStructure(relativePath, lines));
                var referenced = referenceCorpus.Values.Any(
                    text => text.Contains(relativePath, StringComparison.Ordinal));
                if (!referenced)
                {
                    violations.Add(new StaleNarrativeRules.Violation(
                        relativePath, 0, 4, relativePath,
                        "decision file is referenced from no class doc or agent document"));
                }
            }
        }

        violations.Should().BeEmpty(BuildFailureMessage(violations));
    }

    // Rule 4's corpus: src/**/*.cs, tests/**/*.cs, AGENTS.md, and docs/ai/*.md at the top level only
    // (not docs/ai/decisions itself, which is what rule 4 checks for being cited).
    private static Dictionary<string, string> BuildReferenceCorpus(string repoRoot)
    {
        string[] rootFiles = ["AGENTS.md"];
        string[] rootDirs = ["src", "tests", "docs/ai"];
        var files = RepositoryFiles.Enumerate(repoRoot, rootFiles, rootDirs, IsReferenceCorpusMember);
        var corpus = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var relativePath in files)
            corpus[relativePath] = File.ReadAllText(Path.Combine(repoRoot, relativePath));
        return corpus;
    }

    private static bool IsReferenceCorpusMember(string relativePath)
    {
        if (relativePath == "AGENTS.md")
            return true;

        var ext = Path.GetExtension(relativePath).ToLowerInvariant();
        if (ext == ".cs" && IsUnderAny(relativePath, "src/", "tests/"))
            return true;
        if (ext == ".md" && relativePath.StartsWith("docs/ai/", StringComparison.Ordinal))
            return !relativePath["docs/ai/".Length..].Contains('/');
        return false;
    }

    private static bool IsUnderAny(string relativePath, params string[] prefixes) =>
        prefixes.Any(prefix => relativePath.StartsWith(prefix, StringComparison.Ordinal));

    private static string BuildFailureMessage(IReadOnlyList<StaleNarrativeRules.Violation> violations)
    {
        var sorted = violations
            .OrderBy(v => v.RelativePath, StringComparer.Ordinal)
            .ThenBy(v => v.LineNumber)
            .ToList();
        var lines = new List<string> { $"{sorted.Count} stale-narrative violation(s):" };
        foreach (var v in sorted)
        {
            var location = v.LineNumber == 0 ? v.RelativePath : $"{v.RelativePath}:{v.LineNumber}";
            lines.Add($"{location} [rule {v.Rule}] \"{v.Match}\" — {v.Detail}");
        }
        lines.Add(
            "Rule text: docs/ai/conventions.md, \"Only the current state\". Mark a legitimate " +
            "current-state use with narrative-guard:allow: <reason> on the same line.");

        return string.Join(Environment.NewLine, lines);
    }
}
