// tests/Struo.Tests/Architecture/ApiItemServiceBoundaryTests.cs
using System.Text;
using System.Text.RegularExpressions;
using AwesomeAssertions;
using Xunit;

namespace Struo.Tests.Architecture;

/// <summary>
/// Layering guard: nothing in <c>Struo.Api</c> names the concrete <c>ItemService</c> in code. Controllers
/// and GraphQL resolvers take <c>IItemUseCases</c>, so a replacement registered for that interface
/// serves REST and GraphQL alike. The concrete class is registered in <c>Struo.Infrastructure</c>, which
/// this guard does not scan. A repo-shape check (it reads the Api sources on disk) that ignores
/// comments and string literals, so a doc comment or a message may still mention the class.
/// </summary>
public sealed partial class ApiItemServiceBoundaryTests
{
    [GeneratedRegex(@"\bItemService\b", RegexOptions.Compiled)]
    private static partial Regex ConcreteName();

    [Fact]
    public void No_api_type_depends_on_the_concrete_item_service()
    {
        var apiDir = FindApiDir();
        apiDir.Should().NotBeNull("expected to find src/Struo.Api above the test output");

        var files = Directory.GetFiles(apiDir!, "*.cs", SearchOption.AllDirectories)
            .Where(f => !IsBuildOutput(f))
            .ToList();
        files.Should().NotBeEmpty("the guard is worthless if it finds no sources to check");

        var offenders = new List<string>();
        foreach (var file in files)
            offenders.AddRange(ViolationLines(File.ReadAllText(file)).Select(n => $"{Path.GetFileName(file)}:{n}"));

        offenders.Should().BeEmpty(
            "Struo.Api must depend on IItemUseCases, not on the concrete ItemService, so that a " +
            "replacement registered for IItemUseCases serves REST and GraphQL alike.");
    }

    [Fact]
    public void A_url_in_a_string_does_not_hide_the_rest_of_the_line()
    {
        ViolationLines("var u = \"https://x\"; services.AddScoped<ItemService>();").Should().Equal(1);
    }

    [Theory]
    [InlineData("var s = \"ItemService\";")]
    [InlineData("var s = \"a \\\" ItemService\";")]
    [InlineData("var s = @\"ItemService \"\" ItemService\";")]
    [InlineData("var s = $\"x {1} ItemService\";")]
    [InlineData("var s = $@\"ItemService\";")]
    [InlineData("var s = \"\"\"\nItemService \"quoted\"\n\"\"\";")]
    [InlineData("var c = '\"'; // ItemService")]
    [InlineData("// ItemService")]
    [InlineData("/* ItemService\n ItemService */")]
    [InlineData("var s = \"/*\"; // ItemService */")]
    public void Names_inside_literals_and_comments_are_ignored(string source) =>
        ViolationLines(source).Should().BeEmpty();

    [Theory]
    [InlineData("var s = \"/*\"; ItemService x;", 1)]
    [InlineData("var c = '\"'; ItemService x;", 1)]
    [InlineData("var s = @\"a\\\"; ItemService x;", 1)]
    [InlineData("var s = \"a\\\\\"; ItemService x;", 1)]
    [InlineData("/* a\n b */ ItemService x;", 2)]
    [InlineData("var s = \"\"\"\n\"\"\"; ItemService x;", 2)]
    public void Code_after_a_literal_or_comment_is_still_scanned(string source, int line) =>
        ViolationLines(source).Should().Equal(line);

    private static IEnumerable<int> ViolationLines(string source)
    {
        var lines = StripLiteralsAndComments(source).Split('\n');
        for (var i = 0; i < lines.Length; i++)
            if (ConcreteName().IsMatch(lines[i])) yield return i + 1;
    }

    /// <summary>
    /// Replaces comments with nothing and string and char literals (regular, verbatim, interpolated, raw)
    /// with <c>""</c>, keeping every newline so line numbers survive. Literals are read before comments,
    /// so a <c>//</c> or <c>/*</c> inside a string starts nothing.
    /// </summary>
    private static string StripLiteralsAndComments(string s)
    {
        var o = new StringBuilder(s.Length);
        var i = 0;
        while (i < s.Length)
        {
            var c = s[i];
            if (c == '/' && Next(s, i) == '/')
            {
                while (i < s.Length && s[i] != '\n') i++;
            }
            else if (c == '/' && Next(s, i) == '*')
            {
                var end = s.IndexOf("*/", i + 2, StringComparison.Ordinal);
                var stop = end < 0 ? s.Length : end + 2;
                AppendNewlines(o, s, i, stop);
                i = stop;
            }
            else if (c == '\'')
            {
                i = SkipQuoted(s, i, '\'', o);
                o.Append("''");
            }
            else if (c == '"')
            {
                i = SkipString(s, i, o);
                o.Append("\"\"");
            }
            else
            {
                o.Append(c);
                i++;
            }
        }
        return o.ToString();
    }

    private static char Next(string s, int i) => i + 1 < s.Length ? s[i + 1] : '\0';

    private static void AppendNewlines(StringBuilder o, string s, int from, int to)
    {
        for (var k = from; k < to; k++)
            if (s[k] == '\n') o.Append('\n');
    }

    // Returns the index after the literal that opens with the quote at `i`.
    private static int SkipString(string s, int i, StringBuilder o)
    {
        var run = 0;
        while (i + run < s.Length && s[i + run] == '"') run++;
        if (run >= 3)
        {
            var closer = new string('"', run);
            var end = s.IndexOf(closer, i + run, StringComparison.Ordinal);
            var stop = end < 0 ? s.Length : end + run;
            AppendNewlines(o, s, i, stop);
            return stop;
        }

        var verbatim = (i >= 1 && s[i - 1] == '@') || (i >= 2 && s[i - 1] == '$' && s[i - 2] == '@');
        if (!verbatim) return SkipQuoted(s, i, '"', o);

        for (var k = i + 1; k < s.Length; k++)
        {
            if (s[k] == '\n') o.Append('\n');
            if (s[k] != '"') continue;
            if (Next(s, k) == '"') { k++; continue; }
            return k + 1;
        }
        return s.Length;
    }

    // A regular string or char literal: backslash escapes, and an unterminated one ends at the line break.
    private static int SkipQuoted(string s, int i, char quote, StringBuilder o)
    {
        for (var k = i + 1; k < s.Length; k++)
        {
            if (s[k] == '\\') { k++; continue; }
            if (s[k] == '\n') return k;
            if (s[k] == quote) return k + 1;
        }
        return s.Length;
    }

    private static bool IsBuildOutput(string path) =>
        path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
        || path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal);

    // Walk up from BaseDirectory to the first ancestor holding src/Struo.Api.
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
