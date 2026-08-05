using System.Text.RegularExpressions;
using AwesomeAssertions;
using Struo.Tests.Support;
using Xunit;

namespace Struo.Tests.Documentation;

/// <summary>
/// Regression lock for the citation convention in <c>docs/ai/conventions.md</c> ("Citing code from
/// docs and comments"): a citation into this repository's own code must name a construct, not a line
/// range, because line ranges rot silently the first time someone inserts above them. Tasks 3-7 of the
/// 2026-08-04 batch removed ~100 such citations; without this guard the next insertion quietly brings
/// them back, which is exactly what happened both in the batch this test belongs to (a doc-comment
/// insertion broke two chapter-12 line citations) and in the batch before it.
/// </summary>
/// <remarks>
/// <para>
/// <b>What this test enforces, and what it does not.</b> Investigation while writing this guard found
/// citations come in four shapes; only two are mechanically distinguishable from ordinary prose without
/// false positives:
/// </para>
/// <list type="number">
/// <item><b>Extension-anchored</b> (<c>SomeFile.cs:123</c>, <c>:123-145</c>, <c>:156,176,273</c>) —
/// ENFORCED. A file-extension immediately before the colon is what makes this safe: nothing else in the
/// scanned corpus produces `&lt;identifier&gt;.cs:&lt;digits&gt;`, so there is no ambiguity with a port
/// (<c>:5221</c>), a time, or a version string.</item>
/// <item><b>Construct-with-range, no extension</b> (e.g. <c>FilesController.Download</c> paired with a
/// hyphenated range such as <c>74-163</c> right after the colon) — ENFORCED.
/// A dotted, capitalized identifier chain followed by <c>:</c> and a HYPHENATED range is likewise not
/// produced by anything else observed in this repository (ports and aspect ratios are bare numbers with
/// no leading dotted identifier; config-key paths like <c>Database:ConnectionString</c> carry no digits).
/// Unlike the extension-anchored form, this shape never carries a real file to resolve — this
/// repository's own upstream citations are always written extension-anchored (see the eight upstream
/// citations resolved below) — so every match here is treated as a violation unconditionally, with no
/// existence check.</item>
/// <item><b>Bare continuation</b> (a lone <c>`:145`</c> or <c>`74-163`</c> sitting in the same sentence
/// as an earlier full citation, carrying no filename of its own) — NOT ENFORCED. A bare <c>:145</c> is
/// not reliably distinguishable from a port (<c>:5221</c>, <c>:9000</c>, <c>:6363</c>) or a time, and a
/// bare <c>74-163</c> is not reliably distinguishable from an aspect ratio (<c>64:48</c>) or any other
/// hyphenated pair of numbers in prose. A reviewer must still check by hand for a bare number trailing a
/// citation sentence.</item>
/// <item><b>Prose</b> ("line 74", 「第 74 行」) — NOT ENFORCED. Unbounded natural-language surface; a
/// reliable pattern would need to enumerate every phrasing in two languages and would still miss
/// rewordings. Swept for by hand during Tasks 3-7; none were found, but nothing here re-checks that.</item>
/// </list>
/// <para>
/// <b>The upstream exception, expressed mechanically.</b> <c>docs/ai/conventions.md</c> permits
/// citations into pinned upstream SqlSugar source (e.g.
/// <c>src/Struo.Infrastructure/Persistence/ColumnTypeMap.cs</c>), because that source is version-pinned
/// and cannot shift underneath us. For the extension-anchored form, a match is a violation only if the
/// cited file's BASENAME exists somewhere under <c>src/</c>, <c>tests/</c>, <c>frontend/src/</c>,
/// <c>schema/</c> or <c>db/</c> in this repository. There is deliberately no hard-coded allowlist of
/// upstream filenames: that would go stale, and it would hide a new own-repo citation that happened to
/// collide with an upstream name. As of this writing there are exactly eight such upstream citations
/// (seven in <c>ColumnTypeMap.cs</c>, one in <see cref="Struo.Tests.Persistence.DatabaseInitializerTests"/>,
/// and one shared line each in chapter 15 of both manual languages) whose basenames
/// (<c>EntityMaintenance.cs</c>, <c>SqlServerDbMaintenance.cs</c>, <c>MySqlDbMaintenance.cs</c>,
/// <c>SqliteCodeFirst.cs</c>, <c>Methods.cs</c>, <c>SqliteDbMaintenance.cs</c>) resolve nowhere in this
/// repository and so stay green.
/// </para>
/// <para>
/// <b>Scan roots</b>: <c>AGENTS.md</c>, <c>CLAUDE.md</c>, <c>docs/ai/**</c>, <c>docs/guide/**</c>,
/// <c>src/**/*.cs</c>, <c>tests/**/*.cs</c>, <c>frontend/src/**</c>. <b>Excluded</b>:
/// <c>docs/_archive-local/**</c> (untracked, deliberately frozen historical records — including this
/// very task's own plan file, which is full of line citations by design), any <c>obj</c>/<c>bin</c>
/// directory, and <c>node_modules</c>. A single walk from <see cref="RepoRoot.Find"/> both collects the
/// files to scan and builds the basename set used for the upstream-exception resolution, so the cost
/// stays one directory traversal plus cheap per-line regex matching.
/// </para>
/// </remarks>
public sealed class CodeCitationConventionTests
{
    /// <summary>
    /// Extensions recognized as "this token names a file" for the extension-anchored form. Deliberately
    /// covers the file types actually cited in this repository's docs/comments (verified against the
    /// full corpus while designing this test, with zero false positives at this breadth) rather than
    /// every extension that exists anywhere, since a broader list only matters if something in the
    /// corpus would collide with it.
    /// </summary>
    private static readonly HashSet<string> KnownFileExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        "cs", "ts", "tsx", "vue", "md", "sql", "json", "yml", "yaml",
        "css", "scss", "html", "cshtml", "razor", "csproj", "xml", "config",
    };

    /// <summary>
    /// Form 1: an extension-anchored citation. The colon must sit directly against the extension (no
    /// intervening whitespace) — every real citation in this repository is written that way, and
    /// requiring it avoids the test manufacturing its own false positives out of coincidental
    /// "word.ext" followed eventually by "some other colon" prose.
    /// </summary>
    private static readonly Regex ExtensionAnchoredCitation = new(
        @"(?<path>[A-Za-z0-9_][A-Za-z0-9_./\\-]*\.(?:cs|ts|tsx|vue|md|sql|json|ya?ml|css|scss|html|cshtml|razor|csproj|xml|config)):(?<lines>\d+(?:[,-]\d+)*)",
        RegexOptions.Compiled);

    /// <summary>
    /// Form 3: a dotted, capitalized construct citing a hyphenated line range with no file extension
    /// (e.g. <c>FilesController.Download</c> immediately followed by <c>:74-163</c>). Requires at least two dotted segments and an
    /// uppercase lead so it cannot match a bare config-key path or a single identifier; requires a
    /// hyphenated range (not a single number) so it cannot match a version-like or enum-like token.
    /// </summary>
    private static readonly Regex ConstructWithRangeCitation = new(
        @"\b(?<construct>[A-Z][A-Za-z0-9_]*(?:\.[A-Za-z0-9_]+)+):(?<range>\d+-\d+)\b",
        RegexOptions.Compiled);

    private static readonly string[] ScanRootFiles = ["AGENTS.md", "CLAUDE.md"];
    private static readonly string[] ScanRootDirs = ["docs/ai", "docs/guide", "frontend/src"];
    private static readonly string[] CsOnlyScanRootDirs = ["src", "tests"];
    private static readonly string[] ResolutionRootDirs = ["src", "tests", "frontend/src", "schema", "db"];
    private static readonly string[] ExcludedDirNames = ["obj", "bin", "node_modules"];
    private const string ExcludedRelativePrefix = "docs/_archive-local";

    private sealed record Violation(string RelativePath, int LineNumber, string Citation, string Reason)
    {
        public override string ToString() => $"{RelativePath}:{LineNumber} → {Citation} ({Reason})";
    }

    [Fact]
    public void No_line_number_citations_into_files_that_exist_in_this_repository()
    {
        var repoRoot = RepoRoot.Find();
        var (scanFiles, repoBasenames) = WalkRepositoryOnce(repoRoot);

        var violations = new List<Violation>();
        foreach (var relativePath in scanFiles)
        {
            var lines = File.ReadAllLines(Path.Combine(repoRoot, relativePath));
            for (var i = 0; i < lines.Length; i++)
            {
                CollectExtensionAnchoredViolations(relativePath, i + 1, lines[i], repoBasenames, violations);
                CollectConstructWithRangeViolations(relativePath, i + 1, lines[i], violations);
            }
        }

        violations.Should().BeEmpty(
            "a citation into a file that exists in this repository must name a construct, not a line " +
            "number (docs/ai/conventions.md, \"Citing code from docs and comments\") — violations:\n" +
            string.Join("\n", violations));
    }

    private static void CollectExtensionAnchoredViolations(
        string relativePath, int lineNumber, string line, HashSet<string> repoBasenames, List<Violation> violations)
    {
        foreach (Match match in ExtensionAnchoredCitation.Matches(line))
        {
            var path = match.Groups["path"].Value;
            var basename = path.Replace('\\', '/').Split('/')[^1];
            if (repoBasenames.Contains(basename))
            {
                violations.Add(new Violation(relativePath, lineNumber, match.Value,
                    $"'{basename}' exists in this repository"));
            }
        }
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
    /// regex scanning; a file under a resolution root contributes its basename to the set used to
    /// decide whether an extension-anchored citation names an own-repo file or an upstream one.
    /// The two root sets overlap (<c>src/**/*.cs</c> and <c>tests/**/*.cs</c> are both scanned AND
    /// resolved against) but neither is a subset of the other (<c>schema/</c> and <c>db/</c> resolve
    /// but are not scanned; <c>docs/ai/**</c> and <c>docs/guide/**</c> scan but do not resolve), so both
    /// checks are evaluated independently per file rather than one being derived from the other.
    /// </summary>
    private static (List<string> ScanFiles, HashSet<string> RepoBasenames) WalkRepositoryOnce(string repoRoot)
    {
        var scanFiles = new List<string>();
        var repoBasenames = new HashSet<string>(StringComparer.Ordinal);

        foreach (var fileName in ScanRootFiles)
        {
            if (File.Exists(Path.Combine(repoRoot, fileName)))
                scanFiles.Add(fileName);
        }

        Walk(new DirectoryInfo(repoRoot), repoRoot, scanFiles, repoBasenames);

        return (scanFiles, repoBasenames);
    }

    private static void Walk(
        DirectoryInfo dir, string repoRoot, List<string> scanFiles, HashSet<string> repoBasenames)
    {
        var relativeDir = Path.GetRelativePath(repoRoot, dir.FullName).Replace('\\', '/');
        if (relativeDir == ".") relativeDir = "";

        if (relativeDir.StartsWith(ExcludedRelativePrefix, StringComparison.Ordinal))
            return;

        foreach (var file in dir.EnumerateFiles())
        {
            var relativePath = relativeDir.Length == 0 ? file.Name : $"{relativeDir}/{file.Name}";

            if (ResolutionRootDirs.Any(root => IsUnderRoot(relativePath, root)))
                repoBasenames.Add(file.Name);

            var isCsOnlyRoot = CsOnlyScanRootDirs.Any(root => IsUnderRoot(relativePath, root));
            var isUnrestrictedRoot = ScanRootDirs.Any(root => IsUnderRoot(relativePath, root));
            if (isUnrestrictedRoot || (isCsOnlyRoot && file.Extension.Equals(".cs", StringComparison.OrdinalIgnoreCase)))
                scanFiles.Add(relativePath);
        }

        foreach (var subDir in dir.EnumerateDirectories())
        {
            if (ExcludedDirNames.Contains(subDir.Name, StringComparer.OrdinalIgnoreCase))
                continue;
            Walk(subDir, repoRoot, scanFiles, repoBasenames);
        }
    }

    private static bool IsUnderRoot(string relativePath, string root) =>
        relativePath.Equals(root, StringComparison.Ordinal) ||
        relativePath.StartsWith(root + "/", StringComparison.Ordinal);
}
