using System.Linq;
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
/// them back, which has happened twice in this repository (in one instance a doc-comment
/// insertion broke two chapter-12 line citations).
/// </summary>
/// <remarks>
/// <para>
/// <b>What this test enforces, and what it does not.</b> Investigation while writing this guard found
/// citations come in four shapes; only two are mechanically distinguishable from ordinary prose without
/// false positives:
/// </para>
/// <list type="number">
/// <item><b>Extension-anchored</b> (e.g. a filename ending in a known extension, immediately followed
/// by a colon and digits) — ENFORCED. A file-extension immediately before the separator is what makes
/// this safe: nothing else in the scanned corpus produces that shape, so there is no ambiguity with a
/// port (<c>:5221</c>), a time, or a version string. The separator accepts a few equivalent spellings
/// seen or plausible in this repository's own citations: an ordinary colon, a colon with one trailing
/// space, a full-width colon (<c>：</c>, plausible from a CJK IME in the zh-TW manual), an optional
/// <c>L</c> before the digits (<c>:L204</c>), or a GitHub-style <c>#L204</c> anchor.</item>
/// <item><b>Bare continuation</b> (a lone <c>`:145`</c> or <c>`74-163`</c> sitting in the same sentence
/// as an earlier full citation, carrying no filename of its own) — NOT ENFORCED. A bare <c>:145</c> is
/// not reliably distinguishable from a port (<c>:5221</c>, <c>:9000</c>, <c>:6363</c>) or a time, and a
/// bare <c>74-163</c> is not reliably distinguishable from an aspect ratio (<c>64:48</c>) or any other
/// hyphenated pair of numbers in prose. A reviewer must still check by hand for a bare number trailing a
/// citation sentence.</item>
/// <item><b>Construct-with-range, no extension</b> (a dotted identifier chain — case-tolerant, so it
/// also covers camelCase constructs like a frontend store method — immediately followed by a colon and
/// a HYPHENATED range) — ENFORCED, and unconditionally so: see "the upstream exception" below for why
/// this form gets no existence check and therefore no per-citation escape hatch. A dotted identifier
/// chain followed by a hyphenated range is likewise not produced by anything else observed in this
/// repository (ports and aspect ratios are bare numbers with no leading dotted identifier; config-key
/// paths like <c>Database:ConnectionString</c> carry no digits).</item>
/// <item><b>Prose</b> ("line 74", 「第 74 行」) — NOT ENFORCED. Unbounded natural-language surface; a
/// reliable pattern would need to enumerate every phrasing in two languages and would still miss
/// rewordings. Swept for by hand during Tasks 3-7; none were found, but nothing here re-checks that.</item>
/// </list>
/// <para>
/// <b>The upstream exception, expressed mechanically — and its limit.</b>
/// <c>docs/ai/conventions.md</c> permits citations into pinned upstream SqlSugar source (e.g.
/// <c>src/Struo.Infrastructure/Persistence/ColumnTypeMap.cs</c>), because that source is version-pinned
/// and cannot shift underneath us. This exception is checkable ONLY for the extension-anchored form,
/// because only that form names an actual file: a match is a violation iff the cited file resolves to
/// something that exists in this repository. Resolution has two modes, chosen by whether the citation
/// itself carries a directory prefix:
/// <list type="bullet">
/// <item>No prefix (a bare filename, e.g. <c>SqlServerDbMaintenance.cs:498</c>, as several of this
/// repository's own upstream citations are written) — violation iff that BASENAME exists anywhere
/// under a resolution root. This is the brief-mandated mechanism, and it is deliberately imprecise: a
/// same-named own-repo file anywhere under a resolution root turns a bare upstream citation red. A
/// future fork that adds e.g. <c>Methods.cs</c> under <c>src/</c> will see this happen, and the correct
/// response is to re-anchor the citation with a directory prefix (below), not to delete the pinned
/// evidence.</item>
/// <item>Has a prefix (e.g. <c>Realization/Sqlite/CodeFirst/SqliteCodeFirst.cs:24-27</c>, as this
/// repository's own upstream citations are usually written) — violation iff some file under a
/// resolution root has a relative path whose trailing path SEGMENTS equal the cited path's segments
/// exactly (see <see cref="PathSuffixMatches"/>). A same-named own-repo file elsewhere (e.g. a decoy
/// <c>src/Struo.Api/SqliteCodeFirst.cs</c>) does not match this suffix and stays green, because its
/// path does not end in <c>CodeFirst/SqliteCodeFirst.cs</c>.</item>
/// </list>
/// There is deliberately no hard-coded allowlist of upstream filenames either way: that would go stale,
/// and it would hide a new own-repo citation that happened to collide with an upstream name.
/// </para>
/// <para>
/// The construct-with-range form (form 3 above) gets NO existence check and therefore no upstream
/// escape hatch at all: it names no file, only a member, so there is nothing to resolve against the
/// mechanism described above. Every match in that form is a violation, unconditionally. This is safe
/// only because this repository has never written an upstream citation that way — every one of its
/// upstream citations is extension-anchored (see the count below). If a genuine upstream citation ever
/// needs to be written for a bare construct, it must be re-written extension-anchored instead of relying
/// on this form; the form itself carries no existence-based way to mark an exception. A fork can still
/// hit a false positive here on ordinary prose — e.g. "Supported Node.js: 20-22" (citation-guard:allow)
/// in <c>docs/guide/**</c>, where <c>js</c> is not in <see cref="KnownFileExtensions"/> so the
/// extension-anchored form never intercepts it first — and the only way out for that line is the same
/// <c>citation-guard:allow</c> marker documented below; there is no existence check to appeal to for
/// this form specifically.
/// </para>
/// <para>
/// As of this writing there are 8 upstream SqlSugar-citation OCCURRENCES across 8 distinct
/// citation texts: 7 in <c>ColumnTypeMap.cs</c> and 1 in
/// <see cref="Struo.Tests.Persistence.DatabaseInitializerTests"/>.
/// Their basenames (<c>EntityMaintenance.cs</c>, <c>SqlServerDbMaintenance.cs</c>,
/// <c>MySqlDbMaintenance.cs</c>, <c>SqliteCodeFirst.cs</c>) resolve nowhere in this repository
/// (checked against the widened resolution roots below too) and so all 8 stay green.
/// </para>
/// <para>
/// <b>Scan roots</b>: <c>AGENTS.md</c>, <c>CLAUDE.md</c>, <c>docs/ai/**</c>, <c>docs/guide/**</c>,
/// <c>src/**/*.cs</c>, <c>tests/**/*.cs</c>, <c>frontend/src/**</c>.
/// <b>Resolution roots</b> (existence/path-suffix checks, independent of the scan-root list — wider,
/// because a citation into e.g. <c>docs/ai/conventions.md</c> or a sample collection rots exactly like
/// one into <c>src/</c> even though this test does not scan either of those two FOR citations):
/// <c>src/</c>, <c>tests/</c>, <c>frontend/src/</c>, <c>schema/</c>, <c>db/</c>, <c>docs/</c>,
/// <c>samples/</c>, plus the root files <c>AGENTS.md</c> and <c>CLAUDE.md</c> themselves.
/// <b>Excluded everywhere</b>: <c>docs/_archive-local/**</c> (untracked historical planning and audit
/// records, e.g. <c>admin-ux-issues-backlog.md</c> — deliberately frozen, and now genuinely load-bearing
/// since <c>docs/</c> became a resolution root: without this exclusion, a citation into an archived
/// record would be judged "exists in this repository" even though the directory is untracked), any
/// <c>obj</c>/<c>bin</c>/<c>.git</c> directory, and <c>node_modules</c>. A single walk from
/// <see cref="RepoRoot.Find"/> both collects the files to scan and builds the resolution index, so the
/// cost stays one directory traversal plus cheap per-line regex matching (measured at 206-265ms for this
/// repository's current size).
/// </para>
/// <para>
/// <b>Self-reference escape.</b> A line containing the literal marker <c>citation-guard:allow</c> is
/// skipped by both regexes entirely, so this doc comment's own illustrative examples below can be
/// written literally instead of as prose paraphrases that could drift from what the regexes actually
/// match. It is not a general-purpose suppression: nothing about a real citation elsewhere in this
/// repository would ever carry that marker, so it cannot be used to launder an actual violation. Fenced
/// code blocks are NOT given any such exemption — a citation inside a fence is a real citation and must
/// stay caught.
/// </para>
/// <para>
/// Examples, kept genuinely literal via the marker above:
/// <c>SomeFile.cs:123</c> (citation-guard:allow) — extension-anchored, resolves nowhere, stays green,
/// exercising the same path a real upstream citation takes.
/// <c>FilesController.Download:74-163</c> (citation-guard:allow) — construct-with-range, always a
/// violation regardless of existence.
/// The four extension-anchored separator variants this test also catches (citation-guard:allow): <c>Program.cs: 204</c>, <c>ColumnTypeMap.cs：66</c>, <c>Program.cs#L204</c>, <c>Program.cs:L204</c>.
/// <c>useItemStore.fetch:74-163</c> (citation-guard:allow) — a camelCase construct-with-range, the shape
/// this test's case-tolerance exists for since <c>frontend/src</c> identifiers are camelCase by
/// convention.
/// </para>
/// </remarks>
public sealed class CodeCitationConventionTests
{
    /// <summary>
    /// A line carrying this literal marker is skipped by both regexes below. See the class remarks
    /// ("Self-reference escape") for why this cannot be used to hide a real citation.
    /// </summary>
    private const string AllowMarker = "citation-guard:allow";

    /// <summary>
    /// Extensions recognized as "this token names a file" for the extension-anchored form. Deliberately
    /// covers the file types actually cited in this repository's docs/comments (verified against the
    /// full corpus while designing this test, with zero false positives at this breadth) rather than
    /// every extension that exists anywhere, since a broader list only matters if something in the
    /// corpus would collide with it. <see cref="ExtensionAnchoredCitation"/>'s regex alternation is
    /// built directly from this set (see its declaration below), so the two cannot drift apart silently
    /// — adding an extension here is the only step needed to also recognize it in the regex.
    /// </summary>
    private static readonly HashSet<string> KnownFileExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        "cs", "ts", "tsx", "vue", "md", "sql", "json", "yml", "yaml",
        "css", "scss", "html", "cshtml", "razor", "csproj", "xml", "config",
    };

    /// <summary>
    /// Form 1: an extension-anchored citation. The separator accepts an ordinary colon, a colon
    /// followed by exactly one space, a full-width colon, an optional <c>L</c> before the digits, or a
    /// <c>#L</c> anchor — the spellings actually found or plausible in this repository (see class
    /// remarks). No separator variant allows more than one space or any other character in between,
    /// which is what keeps this from manufacturing its own false positives out of coincidental
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
            // Bare filename: basename-only, matching every existing file regardless of its own path
            // (the brief-mandated mechanism, imprecise by design — see class remarks). Prefixed
            // citation: only a real path-suffix match counts, so a same-named decoy elsewhere in the
            // repository cannot force this red.
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
    /// to the index used to decide whether an extension-anchored citation names an own-repo file or an
    /// upstream one. The two root sets overlap (<c>src/**/*.cs</c> and <c>tests/**/*.cs</c> are both
    /// scanned AND resolved against) but neither is a subset of the other (<c>schema/</c>, <c>db/</c>,
    /// <c>docs/</c> and <c>samples/</c> resolve but are not scanned; <c>docs/ai/**</c> and
    /// <c>docs/guide/**</c> scan but — deliberately, see class remarks — resolving against only their
    /// own two subdirectories would miss e.g. a stray citation into <c>docs/README.md</c>, so all of
    /// <c>docs/</c> resolves), so both checks are evaluated independently per file rather than one
    /// being derived from the other.
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
