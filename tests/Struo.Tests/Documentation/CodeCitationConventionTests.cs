using System.Linq;
using System.Text.RegularExpressions;
using AwesomeAssertions;
using Struo.Tests.Support;
using Xunit;

namespace Struo.Tests.Documentation;

/// <summary>
/// Enforces the citing convention (docs/ai/conventions.md, "Citing code from docs and comments") for two
/// mechanically safe shapes — an extension-anchored file:line citation, and a dotted construct followed
/// by a hyphenated range — never a bare trailing number or prose ("line 74"), since a line range rots
/// silently the first time someone inserts above it. The upstream-SqlSugar exception is checkable only
/// for the extension-anchored form: a match is a violation iff the cited file resolves in this
/// repository. A line carrying <c>citation-guard:allow</c> is skipped by both regexes.
/// </summary>
public sealed class CodeCitationConventionTests
{
    /// <summary>
    /// A line carrying this literal marker is skipped by both citation regexes below; no real citation
    /// in this repository carries it, so it cannot be used to launder an actual violation.
    /// </summary>
    private const string AllowMarker = "citation-guard:allow";

    /// <summary>
    /// Extensions recognized as "this token names a file" for the extension-anchored form.
    /// <see cref="ExtensionAnchoredCitation"/>'s regex alternation is built directly from this set (see
    /// its declaration below), so the two cannot drift apart silently — adding an extension here is the
    /// only step needed to also recognize it in the regex.
    /// </summary>
    private static readonly HashSet<string> KnownFileExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        "cs", "ts", "tsx", "vue", "md", "sql", "json", "yml", "yaml",
        "css", "scss", "html", "cshtml", "razor", "csproj", "xml", "config",
    };

    /// <summary>
    /// Form 1: an extension-anchored citation. The separator accepts an ordinary colon, a colon
    /// followed by exactly one space, a full-width colon, an optional <c>L</c> before the digits, or a
    /// <c>#L</c> anchor. No separator variant allows more than one space or any other character in
    /// between, which is what keeps this from manufacturing its own false positives out of coincidental
    /// "word.ext" followed eventually by some unrelated colon in prose.
    /// </summary>
    private static readonly Regex ExtensionAnchoredCitation = new(
        $@"(?<path>[A-Za-z0-9_][A-Za-z0-9_./\\-]*\.(?:{string.Join('|', KnownFileExtensions.Select(Regex.Escape))}))(?:[:：]\s?L?|#L)(?<lines>\d+(?:[,-]\d+)*)",
        RegexOptions.Compiled);

    /// <summary>
    /// Form 3: a dotted construct citing a hyphenated line range with no file extension (e.g.
    /// <c>FilesController.Download</c> or, case-tolerantly, a camelCase frontend construct like
    /// <c>useItemStore.fetch</c>, each immediately followed by a colon and a range). Requires at least
    /// two dotted segments so it cannot match a bare config-key path or a single identifier; requires a
    /// hyphenated range (not a single number) so it cannot match a version-like or enum-like token.
    /// Case-tolerant on the leading character specifically so it also covers <c>frontend/src</c>, where
    /// identifiers are camelCase by convention rather than PascalCase.
    /// </summary>
    private static readonly Regex ConstructWithRangeCitation = new(
        @"\b(?<construct>[A-Za-z_][A-Za-z0-9_]*(?:\.[A-Za-z0-9_]+)+)(?:[:：]\s?)(?<range>\d+-\d+)\b",
        RegexOptions.Compiled);

    private static readonly string[] ScanRootFiles = ["AGENTS.md", "CLAUDE.md"];
    private static readonly string[] ScanRootDirs = ["docs/ai", "docs/guide", "frontend/src"];
    private static readonly string[] CsOnlyScanRootDirs = ["src", "tests"];
    private static readonly string[] ResolutionRootFiles = ["AGENTS.md", "CLAUDE.md"];
    private static readonly string[] ResolutionRootDirs =
        ["src", "tests", "frontend/src", "schema", "db", "docs", "samples"];
    private static readonly string[] ExcludedDirNames = ["obj", "bin", "node_modules", ".git"];
    private const string ExcludedRelativePrefix = "docs/_archive-local";

    private sealed record Violation(string RelativePath, int LineNumber, string Citation, string Reason)
    {
        public override string ToString() => $"{RelativePath}:{LineNumber} → {Citation} ({Reason})";
    }

    [Fact]
    public void No_line_number_citations_into_files_that_exist_in_this_repository()
    {
        var repoRoot = RepoRoot.Find();
        var (scanFiles, repoFilesByBasename) = WalkRepositoryOnce(repoRoot);

        var violations = new List<Violation>();
        foreach (var relativePath in scanFiles)
        {
            var lines = File.ReadAllLines(Path.Combine(repoRoot, relativePath));
            for (var i = 0; i < lines.Length; i++)
            {
                if (lines[i].Contains(AllowMarker, StringComparison.Ordinal))
                    continue;

                CollectExtensionAnchoredViolations(relativePath, i + 1, lines[i], repoFilesByBasename, violations);
                CollectConstructWithRangeViolations(relativePath, i + 1, lines[i], violations);
            }
        }

        violations.Should().BeEmpty(
            "a citation into a file that exists in this repository must name a construct, not a line " +
            "number (docs/ai/conventions.md, \"Citing code from docs and comments\") — violations:\n" +
            string.Join("\n", violations));
    }

    // Exercises the four separator spellings the class doc claims Form 1 accepts, and the "violation
    // iff the basename resolves in this repository" rule the upstream exception depends on: an unknown
    // basename (SomeFile.cs) stays green, a known one (Program.cs/ColumnTypeMap.cs) goes red regardless
    // of which separator spelling introduced it.
    [Theory]
    [InlineData("SomeFile.cs:123", false)]
    [InlineData("Program.cs: 204", true)] // citation-guard:allow: literal regex fixture, not a real citation
    [InlineData("ColumnTypeMap.cs：66", true)] // citation-guard:allow: literal regex fixture, not a real citation
    [InlineData("Program.cs#L204", true)] // citation-guard:allow: literal regex fixture, not a real citation
    [InlineData("Program.cs:L204", true)] // citation-guard:allow: literal regex fixture, not a real citation
    public void Extension_anchored_citation_is_a_violation_only_when_the_basename_resolves_in_this_repository(
        string line, bool expectViolation)
    {
        var repoFilesByBasename = new Dictionary<string, List<string>>(StringComparer.Ordinal)
        {
            ["Program.cs"] = ["src/Struo.Api/Program.cs"],
            ["ColumnTypeMap.cs"] = ["src/Struo.Infrastructure/Persistence/ColumnTypeMap.cs"],
        };
        var violations = new List<Violation>();

        CollectExtensionAnchoredViolations("doc.md", 1, line, repoFilesByBasename, violations);

        violations.Should().HaveCount(expectViolation ? 1 : 0);
    }

    // Form 3 gets no existence check, so it is a violation unconditionally — including the documented
    // false positive on an ordinary version-range sentence below, where the extension ".js" is not a
    // known file extension so Form 1 never intercepts it first. A real occurrence of that prose needs a
    // citation-guard:allow marker; this test only proves the shape is caught.
    [Theory]
    [InlineData("FilesController.Download:74-163")] // citation-guard:allow: literal regex fixture, not a real citation
    [InlineData("useItemStore.fetch:74-163")] // citation-guard:allow: literal regex fixture, not a real citation
    [InlineData("Supported Node.js: 20-22")] // citation-guard:allow: literal regex fixture, not a real citation
    public void Construct_with_range_citation_is_always_a_violation_regardless_of_existence(string line)
    {
        var violations = new List<Violation>();

        CollectConstructWithRangeViolations("doc.md", 1, line, violations);

        violations.Should().ContainSingle();
    }

    private static void CollectExtensionAnchoredViolations(
        string relativePath, int lineNumber, string line,
        Dictionary<string, List<string>> repoFilesByBasename, List<Violation> violations)
    {
        foreach (Match match in ExtensionAnchoredCitation.Matches(line))
        {
            var citedPath = match.Groups["path"].Value.Replace('\\', '/');
            var basename = citedPath.Split('/')[^1];
            if (!repoFilesByBasename.TryGetValue(basename, out var candidates))
                continue; // basename resolves nowhere in this repository -> treated as upstream.

            var hasDirectoryPrefix = citedPath.Contains('/');
            // Bare filename: basename-only, matching every existing file regardless of its own path —
            // deliberately imprecise. Prefixed citation: only a real path-suffix match counts, so a
            // same-named decoy elsewhere in the repository cannot force this red.
            var isOwnRepoFile = !hasDirectoryPrefix || candidates.Any(real => PathSuffixMatches(real, citedPath));
            if (!isOwnRepoFile)
                continue;

            violations.Add(new Violation(relativePath, lineNumber, match.Value,
                hasDirectoryPrefix
                    ? $"'{citedPath}' matches a real path in this repository"
                    : $"'{basename}' exists in this repository"));
        }
    }

    /// <summary>
    /// True iff <paramref name="repoRelativePath"/>'s trailing path segments equal
    /// <paramref name="citedPath"/>'s segments exactly, component by component (not a raw substring
    /// match, so <c>Foo/Bar.cs</c> does not spuriously match a repository path ending in
    /// <c>NotFoo/Bar.cs</c>). Both arguments must already use forward slashes.
    /// </summary>
    private static bool PathSuffixMatches(string repoRelativePath, string citedPath)
    {
        var repoSegments = repoRelativePath.Split('/');
        var citedSegments = citedPath.Split('/');
        if (citedSegments.Length > repoSegments.Length)
            return false;

        for (var i = 1; i <= citedSegments.Length; i++)
        {
            if (!string.Equals(repoSegments[^i], citedSegments[^i], StringComparison.Ordinal))
                return false;
        }
        return true;
    }

    private static void CollectConstructWithRangeViolations(
        string relativePath, int lineNumber, string line, List<Violation> violations)
    {
        foreach (Match match in ConstructWithRangeCitation.Matches(line))
        {
            var construct = match.Groups["construct"].Value;
            var lastSegment = construct[(construct.LastIndexOf('.') + 1)..];
            if (KnownFileExtensions.Contains(lastSegment))
                continue; // extension-anchored — already handled above, do not double-report.

            violations.Add(new Violation(relativePath, lineNumber, match.Value,
                "construct-with-range citation carries no file extension to check for the upstream " +
                "exception, and this repository's convention never uses this bare-construct form for " +
                "upstream citations"));
        }
    }

    /// <summary>
    /// One traversal from the repo root serves both needs: a file under a scan root is queued for
    /// regex scanning; a file under a resolution root contributes its basename AND full relative path
    /// to the index that decides whether an extension-anchored citation names an own-repo file or an
    /// upstream one. The two root sets overlap (<c>src/**/*.cs</c> and <c>tests/**/*.cs</c> are both
    /// scanned AND resolved against) but neither is a subset of the other (<c>schema/</c>, <c>db/</c>,
    /// <c>docs/</c> and <c>samples/</c> resolve but are not scanned; <c>docs/ai/**</c> and
    /// <c>docs/guide/**</c> scan but resolving against only their own two subdirectories would miss e.g.
    /// a stray citation into <c>docs/README.md</c>, so all of <c>docs/</c> resolves), so both checks are
    /// evaluated independently per file rather than one being derived from the other.
    /// </summary>
    private static (List<string> ScanFiles, Dictionary<string, List<string>> RepoFilesByBasename) WalkRepositoryOnce(
        string repoRoot)
    {
        var scanFiles = new List<string>();
        var repoFilesByBasename = new Dictionary<string, List<string>>(StringComparer.Ordinal);

        foreach (var fileName in ScanRootFiles)
        {
            if (File.Exists(Path.Combine(repoRoot, fileName)))
                scanFiles.Add(fileName);
        }

        foreach (var fileName in ResolutionRootFiles)
        {
            if (File.Exists(Path.Combine(repoRoot, fileName)))
                AddBasename(repoFilesByBasename, fileName, fileName);
        }

        Walk(new DirectoryInfo(repoRoot), repoRoot, scanFiles, repoFilesByBasename);

        return (scanFiles, repoFilesByBasename);
    }

    private static void AddBasename(Dictionary<string, List<string>> map, string basename, string relativePath)
    {
        if (!map.TryGetValue(basename, out var paths))
        {
            paths = [];
            map[basename] = paths;
        }
        paths.Add(relativePath);
    }

    private static void Walk(
        DirectoryInfo dir, string repoRoot, List<string> scanFiles, Dictionary<string, List<string>> repoFilesByBasename)
    {
        var relativeDir = Path.GetRelativePath(repoRoot, dir.FullName).Replace('\\', '/');
        if (relativeDir == ".") relativeDir = "";

        if (relativeDir.StartsWith(ExcludedRelativePrefix, StringComparison.Ordinal))
            return;

        foreach (var file in dir.EnumerateFiles())
        {
            var relativePath = relativeDir.Length == 0 ? file.Name : $"{relativeDir}/{file.Name}";

            if (ResolutionRootDirs.Any(root => IsUnderRoot(relativePath, root)))
                AddBasename(repoFilesByBasename, file.Name, relativePath);

            var isCsOnlyRoot = CsOnlyScanRootDirs.Any(root => IsUnderRoot(relativePath, root));
            var isUnrestrictedRoot = ScanRootDirs.Any(root => IsUnderRoot(relativePath, root));
            if (isUnrestrictedRoot || (isCsOnlyRoot && file.Extension.Equals(".cs", StringComparison.OrdinalIgnoreCase)))
                scanFiles.Add(relativePath);
        }

        foreach (var subDir in dir.EnumerateDirectories())
        {
            if (ExcludedDirNames.Contains(subDir.Name, StringComparer.OrdinalIgnoreCase))
                continue;
            Walk(subDir, repoRoot, scanFiles, repoFilesByBasename);
        }
    }

    private static bool IsUnderRoot(string relativePath, string root) =>
        relativePath.Equals(root, StringComparison.Ordinal) ||
        relativePath.StartsWith(root + "/", StringComparison.Ordinal);
}
