using System.Text.RegularExpressions;

namespace Struo.Tests.Documentation;

/// <summary>
/// Pure functions behind the repo-wide narrative guard: pull the comment/prose text out of a source
/// line (by language) and check it for change-narrative wording, a bare date, a PR/issue reference,
/// or an allow marker with no reason. Word-boundary matching on <c>//</c> is a heuristic (an odd count
/// of unescaped quote characters before it means the slashes sit inside a string literal), not a real
/// tokenizer, so a pathological line can still fool it.
/// </summary>
internal static class StaleNarrativeRules
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

    private static readonly Regex FenceLine = new(@"^ {0,3}(`{3,}|~{3,})", RegexOptions.Compiled);

    private static readonly Regex HeadingLevel2 = new(@"^##\s+(.*)$", RegexOptions.Compiled);

    private static readonly Regex InlineCodeSpan = new(@"(`+)[^`]*?\1", RegexOptions.Compiled);

    public static SourceKind KindOf(string relativePath) =>
        Path.GetExtension(relativePath).ToLowerInvariant() switch
        {
            ".vue" => SourceKind.Vue,
            ".ts" => SourceKind.TypeScript,
            ".cs" => SourceKind.CSharp,
            ".md" => SourceKind.Markdown,
            var other => throw new ArgumentException($"Unsupported extension '{other}' for '{relativePath}'.", nameof(relativePath)),
        };

    public static IReadOnlyList<ScannableLine> ExtractScannable(SourceKind kind, IReadOnlyList<string> lines) =>
        kind == SourceKind.Markdown ? ExtractMarkdown(lines) : ExtractCode(kind, lines);

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

    private static int? FindHeadingLine(IReadOnlyList<string> lines, string name)
    {
        for (var i = 0; i < lines.Count; i++)
        {
            var match = HeadingLevel2.Match(lines[i].TrimEnd());
            if (match.Success && string.Equals(match.Groups[1].Value.Trim(), name, StringComparison.Ordinal))
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

    private static List<ScannableLine> ExtractMarkdown(IReadOnlyList<string> lines)
    {
        var fenced = ComputeFencedLines(lines);
        var result = new List<ScannableLine>();
        string? currentSection = null;

        for (var i = 0; i < lines.Count; i++)
        {
            if (fenced[i])
                continue;

            var line = lines[i];
            var headingMatch = HeadingLevel2.Match(line);
            if (headingMatch.Success)
                currentSection = headingMatch.Groups[1].Value.Trim();

            var text = InlineCodeSpan.Replace(line, "").Trim();
            result.Add(new ScannableLine(i + 1, text, currentSection));
        }

        return result;
    }

    private static bool[] ComputeFencedLines(IReadOnlyList<string> lines)
    {
        var fenced = new bool[lines.Count];
        (char Char, int Length)? fence = null;

        for (var i = 0; i < lines.Count; i++)
        {
            var opener = FenceLine.Match(lines[i]);
            if (fence is not null)
            {
                fenced[i] = true;
                if (opener.Success)
                {
                    var token = opener.Groups[1].Value;
                    if (token[0] == fence.Value.Char && token.Length >= fence.Value.Length)
                        fence = null;
                }
                continue;
            }

            if (opener.Success)
            {
                fenced[i] = true;
                var token = opener.Groups[1].Value;
                fence = (token[0], token.Length);
            }
        }

        return fenced;
    }

    private static List<ScannableLine> ExtractCode(SourceKind kind, IReadOnlyList<string> lines)
    {
        var result = new List<ScannableLine>(lines.Count);
        var inBlock = false;
        var inHtml = false;

        for (var i = 0; i < lines.Count; i++)
            result.Add(ExtractCodeLine(i + 1, lines[i], kind, ref inBlock, ref inHtml));

        return result;
    }

    private static ScannableLine ExtractCodeLine(int lineNumber, string line, SourceKind kind, ref bool inBlock, ref bool inHtml)
    {
        if (inHtml)
        {
            var close = line.IndexOf("-->", StringComparison.Ordinal);
            if (close < 0)
                return new ScannableLine(lineNumber, line.Trim(), null);
            inHtml = false;
            return new ScannableLine(lineNumber, line[..close].Trim(), null);
        }

        if (inBlock)
        {
            var close = line.IndexOf("*/", StringComparison.Ordinal);
            if (close < 0)
                return new ScannableLine(lineNumber, line.Trim(), null);
            inBlock = false;
            return new ScannableLine(lineNumber, line[..close].Trim(), null);
        }

        var htmlStart = kind == SourceKind.Vue ? line.IndexOf("<!--", StringComparison.Ordinal) : -1;
        var blockStart = line.IndexOf("/*", StringComparison.Ordinal);
        var lineStart = FindLineCommentStart(line);

        var (start, marker) = EarliestMarker(htmlStart, blockStart, lineStart);
        if (start < 0)
            return new ScannableLine(lineNumber, "", null);

        if (marker == 0)
        {
            var close = line.IndexOf("-->", start + 4, StringComparison.Ordinal);
            if (close >= 0)
                return new ScannableLine(lineNumber, line[(start + 4)..close].Trim(), null);
            inHtml = true;
            return new ScannableLine(lineNumber, line[(start + 4)..].Trim(), null);
        }

        if (marker == 1)
        {
            var close = line.IndexOf("*/", start + 2, StringComparison.Ordinal);
            if (close >= 0)
                return new ScannableLine(lineNumber, line[(start + 2)..close].Trim(), null);
            inBlock = true;
            return new ScannableLine(lineNumber, line[(start + 2)..].Trim(), null);
        }

        return new ScannableLine(lineNumber, line[(start + 2)..].Trim(), null);
    }

    private static (int Start, int Marker) EarliestMarker(int htmlStart, int blockStart, int lineStart)
    {
        var start = -1;
        var marker = 0; // 0 = html, 1 = block, 2 = line
        if (htmlStart >= 0) { start = htmlStart; marker = 0; }
        if (blockStart >= 0 && (start < 0 || blockStart < start)) { start = blockStart; marker = 1; }
        if (lineStart >= 0 && (start < 0 || lineStart < start)) { start = lineStart; marker = 2; }
        return (start, marker);
    }

    private static int FindLineCommentStart(string line)
    {
        var searchFrom = 0;
        while (true)
        {
            var idx = line.IndexOf("//", searchFrom, StringComparison.Ordinal);
            if (idx < 0)
                return -1;
            if (CountUnescapedQuotes(line, idx) % 2 == 0)
                return idx;
            searchFrom = idx + 2;
        }
    }

    private static int CountUnescapedQuotes(string line, int endExclusive)
    {
        var count = 0;
        for (var i = 0; i < endExclusive; i++)
        {
            if (line[i] != '"' && line[i] != '\'')
                continue;

            var backslashes = 0;
            var j = i - 1;
            while (j >= 0 && line[j] == '\\')
            {
                backslashes++;
                j--;
            }
            if (backslashes % 2 == 0)
                count++;
        }
        return count;
    }
}
