// tests/Struo.Tests.Template/ValueConditionsConventionTests.cs
using System.Text.RegularExpressions;
using AwesomeAssertions;
using Struo.Tests.Support;
using Xunit;

namespace Struo.Tests.Template;

/// <summary>
/// SqlSugar renders the values of <c>ConditionalType.In</c> and <c>ConditionalType.NotIn</c> as literals inside the
/// SQL text. <c>ValueConditions</c> parses each value to the column's CLR type before it builds one, so that file is
/// the only place in <c>src/</c> allowed to name either member. A mention in a comment counts too: the scan reads
/// text, so the rule holds for code written later without anyone remembering it.
/// </summary>
public sealed partial class ValueConditionsConventionTests
{
    private const string AllowedFile = "src/Struo.Infrastructure/Query/ValueConditions.cs";

    [GeneratedRegex(@"ConditionalType\s*\.\s*(In|NotIn)\b")]
    private static partial Regex SetMember();

    internal static IReadOnlyList<string> Offenders(IEnumerable<(string Path, string Text)> files) =>
        files.Where(f => f.Path != AllowedFile && SetMember().IsMatch(f.Text)).Select(f => f.Path).Order().ToList();

    [Fact]
    public void Only_ValueConditions_names_the_in_and_not_in_condition_types()
    {
        var root = RepoRoot.Find();
        var files = Directory.EnumerateFiles(Path.Combine(root, "src"), "*.cs", SearchOption.AllDirectories)
            .Select(p => (Path: Path.GetRelativePath(root, p).Replace(Path.DirectorySeparatorChar, '/'), Text: File.ReadAllText(p)))
            .Where(f => !f.Path.Contains("/obj/", StringComparison.Ordinal) && !f.Path.Contains("/bin/", StringComparison.Ordinal))
            .ToList();
        files.Should().Contain(f => f.Path == AllowedFile, "the allowed file must exist where this test expects it");

        Offenders(files).Should().BeEmpty(
            "only {0} may name ConditionalType.In or ConditionalType.NotIn; every other site builds its condition through it", AllowedFile);
    }

    [Theory]
    [InlineData("x = ConditionalType.In;", true)]
    [InlineData("x = ConditionalType . NotIn;", true)]
    [InlineData("// ConditionalType.In is the typed primitive", true)]
    [InlineData("/// <see cref=\"ConditionalType.NotIn\"/>", true)]
    [InlineData("x = ConditionalType.Equal;", false)]
    [InlineData("x = ConditionalType.IsNot;", false)]
    [InlineData("x = ConditionalType.InSingle;", false)]
    public void The_scan_reads_code_and_comments_and_nothing_else(string text, bool flagged)
    {
        Offenders([("another file", text)]).Count.Should().Be(flagged ? 1 : 0);
        Offenders([(AllowedFile, text)]).Should().BeEmpty();
    }
}
