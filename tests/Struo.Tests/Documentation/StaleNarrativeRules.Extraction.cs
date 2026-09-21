using System.Text.RegularExpressions;

namespace Struo.Tests.Documentation;

/// <summary>
/// Pulls the comment/prose text out of a source line, by language. A comment start (<c>//</c>,
/// <c>/*</c>, or, for Vue, <c>&lt;!--</c>) is located by the same heuristic: an odd count of unescaped
/// quote characters before it means the marker sits inside a string literal instead of starting a real
/// comment, not a full tokenizer, so a pathological line can still fool it.
/// </summary>
internal static partial class StaleNarrativeRules
{
    [GeneratedRegex(@"^ {0,3}(`{3,}|~{3,})")]
    private static partial Regex FenceLine();

    [GeneratedRegex(@"(`+)[^`]*?\1")]
    private static partial Regex InlineCodeSpan();

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
            var headingMatch = HeadingLevel2().Match(line);
            if (headingMatch.Success)
                currentSection = NormalizeHeadingText(headingMatch.Groups[1].Value);

            var text = InlineCodeSpan().Replace(line, "").Trim();
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
            var opener = FenceLine().Match(lines[i]);
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

        var htmlStart = kind == SourceKind.Vue ? FindMarkerStart(line, "<!--") : -1;
        var blockStart = FindMarkerStart(line, "/*");
        var lineStart = FindMarkerStart(line, "//");

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

        // Line comment: drop any further leading slashes (as in a doc comment) beyond the first two.
        return new ScannableLine(lineNumber, line[(start + 2)..].TrimStart('/').Trim(), null);
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

    private static int FindMarkerStart(string line, string marker)
    {
        var searchFrom = 0;
        while (true)
        {
            var idx = line.IndexOf(marker, searchFrom, StringComparison.Ordinal);
            if (idx < 0)
                return -1;
            if (CountUnescapedQuotes(line, idx) % 2 == 0)
                return idx;
            searchFrom = idx + marker.Length;
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
