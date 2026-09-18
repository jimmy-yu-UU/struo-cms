using System.Text.RegularExpressions;

namespace Struo.Tests.Documentation;

/// <summary>
/// Pure functions behind the repo-wide narrative guard: check a source line's already-extracted
/// comment/prose text (see the extraction partial part) for change-narrative wording, a bare date, a
/// PR/issue reference, or an allow marker with no reason, and check a decision file's raw lines for its
/// required heading structure. An allow marker's reason clears rules 1 and 2 on that line only; rule 3
/// can never be marked.
/// </summary>
internal static partial class StaleNarrativeRules
{
    public enum SourceKind { CSharp, TypeScript, Vue, Markdown }

    public sealed record ScannableLine(int LineNumber, string Text, string? Section);

    public sealed record Violation(string RelativePath, int LineNumber, int Rule, string Match, string Detail);

    public const string AllowMarker = "narrative-guard:allow:";

    private static readonly Regex EnglishBanned = new(
        @"\b(?:used to|previously|formerly|historically|anymore|no longer|now that)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly string[] ChineseBanned =
        ["以前", "曾經", "過去", "原本", "已修", "現在不再", "不再需要"];

    private static readonly Regex BareDate = new(@"\b20\d{2}-\d{2}-\d{2}\b", RegexOptions.Compiled);

    private static readonly Regex PrReference = new(
        @"PR #\d+|pull/\d+|(?<![\w&])#\d{2,}\b", RegexOptions.Compiled);

    private static readonly Regex HeadingLevel2 = new(@"^##\s+(.*)$", RegexOptions.Compiled);

    private static readonly Regex AtxClosingHashes = new(@"\s+#+$", RegexOptions.Compiled);

    /// <summary>
    /// A captured level-2 heading's text, with a trailing ATX closing sequence (e.g. the <c>##</c> in
    /// <c>## Evidence ##</c>) removed — otherwise that heading's <see cref="ScannableLine.Section"/>
    /// would never equal the plain section name a caller checks for.
    /// </summary>
    private static string NormalizeHeadingText(string raw) => AtxClosingHashes.Replace(raw.Trim(), "").Trim();

    public static IReadOnlyList<Violation> Check(
        string relativePath, IReadOnlyList<ScannableLine> lines, bool isDecisionFile)
    {
        var violations = new List<Violation>();
        foreach (var line in lines)
        {
            var text = line.Text;
            if (text.Length == 0)
                continue;

            var (hasMarker, reason) = ParseAllowReason(text);
            var hasReason = hasMarker && reason!.Length > 0;

            var banned = FindBannedWord(text);
            if (banned is not null && !hasReason)
                violations.Add(new Violation(relativePath, line.LineNumber, 1, banned.Value.Match, "change narrative"));

            var dateMatch = BareDate.Match(text);
            if (dateMatch.Success)
            {
                var inDecisionEvidence = isDecisionFile && line.Section == "Evidence";
                if (!inDecisionEvidence && !hasReason)
                {
                    violations.Add(new Violation(relativePath, line.LineNumber, 2, dateMatch.Value,
                        "bare date outside a decision file's Evidence section"));
                }
            }

            foreach (Match prMatch in PrReference.Matches(text))
            {
                violations.Add(new Violation(relativePath, line.LineNumber, 3, prMatch.Value,
                    "PR/issue reference — reword, cannot be marked"));
            }

            if (hasMarker && !hasReason)
                violations.Add(new Violation(relativePath, line.LineNumber, 6, AllowMarker, "allow marker without a reason"));
        }
        return violations;
    }

    public static IReadOnlyList<Violation> CheckDecisionStructure(string relativePath, IReadOnlyList<string> lines)
    {
        var violations = new List<Violation>();
        string[] namedHeadings = ["Decision", "Why", "Evidence", "Unknowns", "Referenced from"];

        var titleLine = FindTitleLine(lines);
        if (titleLine is null)
            violations.Add(new Violation(relativePath, 0, 5, "# ", "missing heading '# <title>'"));

        var lastLine = titleLine ?? 0;
        foreach (var name in namedHeadings)
        {
            var label = $"## {name}";
            var line = FindHeadingLine(lines, name);
            if (line is null)
            {
                violations.Add(new Violation(relativePath, 0, 5, label, $"missing heading '{label}'"));
                continue;
            }

            if (line.Value < lastLine)
                violations.Add(new Violation(relativePath, line.Value, 5, label, $"heading '{label}' out of order"));
            else
                lastLine = line.Value;
        }

        return violations;
    }

    private static int? FindTitleLine(IReadOnlyList<string> lines)
    {
        for (var i = 0; i < lines.Count; i++)
        {
            var trimmed = lines[i].Trim();
            if (trimmed.Length == 0)
                continue;

            return trimmed.StartsWith("# ", StringComparison.Ordinal) && !trimmed.StartsWith("##", StringComparison.Ordinal)
                ? i + 1
                : null;
        }
        return null;
    }

    // A fenced code sample can contain a line that only looks like "## Decision"; skip fenced lines so
    // a sample never satisfies the structure check.
    private static int? FindHeadingLine(IReadOnlyList<string> lines, string name)
    {
        var fenced = ComputeFencedLines(lines);
        for (var i = 0; i < lines.Count; i++)
        {
            if (fenced[i])
                continue;

            var match = HeadingLevel2.Match(lines[i].TrimEnd());
            if (match.Success && string.Equals(NormalizeHeadingText(match.Groups[1].Value), name, StringComparison.Ordinal))
                return i + 1;
        }
        return null;
    }

    private static (string Match, int Index)? FindBannedWord(string text)
    {
        (string Match, int Index)? best = null;

        var englishMatch = EnglishBanned.Match(text);
        if (englishMatch.Success)
            best = (englishMatch.Value, englishMatch.Index);

        foreach (var word in ChineseBanned)
        {
            var idx = text.IndexOf(word, StringComparison.Ordinal);
            if (idx >= 0 && (best is null || idx < best.Value.Index))
                best = (word, idx);
        }

        return best;
    }

    private static (bool HasMarker, string? Reason) ParseAllowReason(string text)
    {
        var idx = text.IndexOf(AllowMarker, StringComparison.Ordinal);
        if (idx < 0)
            return (false, null);

        var raw = text[(idx + AllowMarker.Length)..].Trim();
        if (raw.EndsWith("-->", StringComparison.Ordinal))
            raw = raw[..^3].Trim();
        else if (raw.EndsWith("*/", StringComparison.Ordinal))
            raw = raw[..^2].Trim();

        return (true, raw);
    }
}
