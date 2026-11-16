// tests/Struo.Tests.Template/ValueConditionsConventionTests.cs
using System.Text.RegularExpressions;
using AwesomeAssertions;
using Struo.Tests.Support;
using Xunit;

namespace Struo.Tests.Template;

/// <summary>
/// SqlSugar renders the values of <c>ConditionalType.In</c> and <c>ConditionalType.NotIn</c> as literals inside the
/// SQL text, and reads the texts <c>null</c> and <c>[null]</c> of a <c>ConditionalType.Equal</c> value as a null
/// test. <c>ValueConditions</c> parses each value to the column's CLR type and binds text before it builds one, so
/// that file is the only place in <c>src/</c> meant to name any of the three members. The scan reads text, so
/// it catches a mention in code or in a comment, a <c>using static</c> of the enum, an alias of it, and a
/// <c>ConditionalModel</c> object initializer that names no <c>ConditionalType</c> (the default is
/// <c>Equal</c>). It cannot catch what text does not show: a cast from a number, or a model whose
/// <c>ConditionalType</c> is assigned in a separate statement.
/// </summary>
public sealed partial class ValueConditionsConventionTests
{
    private const string AllowedFile = "src/Struo.Infrastructure/Query/ValueConditions.cs";

    // The one initializer without a ConditionalType outside the allowed file: the model renders its own SQL
    // through CustomConditionalFunc, so its condition type takes no part.
    private const string CustomSqlModelFile = "src/Struo.Infrastructure/Query/OrOfSubqueriesConditional.cs";

    [GeneratedRegex(@"ConditionalType\s*\.\s*(In|NotIn|Equal)\b")]
    private static partial Regex SetMember();

    [GeneratedRegex(@"using\s+static\s+(?:SqlSugar\s*\.\s*)?ConditionalType\s*;")]
    private static partial Regex UsingStatic();

    [GeneratedRegex(@"using\s+\w+\s*=\s*(?:SqlSugar\s*\.\s*)?ConditionalType\s*;")]
    private static partial Regex Alias();

    [GeneratedRegex(@"new\s+ConditionalModel\s*(?:\(\s*\))?\s*\{")]
    private static partial Regex ModelInitializer();

    // True when a ConditionalModel initializer in the text has a body that never writes ConditionalType.
    private static bool HasInitializerWithoutType(string text)
    {
        foreach (Match match in ModelInitializer().Matches(text))
        {
            var depth = 1;
            var end = match.Index + match.Length;
            while (end < text.Length && depth > 0)
            {
                depth += text[end] == '{' ? 1 : text[end] == '}' ? -1 : 0;
                end++;
            }
            if (!text[(match.Index + match.Length)..end].Contains("ConditionalType", StringComparison.Ordinal)) return true;
        }
        return false;
    }

    internal static IReadOnlyList<string> Offenders(IEnumerable<(string Path, string Text)> files) =>
        files.Where(f => f.Path != AllowedFile && (
                SetMember().IsMatch(f.Text) || UsingStatic().IsMatch(f.Text) || Alias().IsMatch(f.Text)
                || (f.Path != CustomSqlModelFile && HasInitializerWithoutType(f.Text))))
            .Select(f => f.Path).Order().ToList();

    [Fact]
    public void Only_ValueConditions_names_the_in_not_in_and_equal_condition_types()
    {
        var root = RepoRoot.Find();
        var files = Directory.EnumerateFiles(Path.Combine(root, "src"), "*.cs", SearchOption.AllDirectories)
            .Select(p => (Path: Path.GetRelativePath(root, p).Replace(Path.DirectorySeparatorChar, '/'), Text: File.ReadAllText(p)))
            .Where(f => !f.Path.Contains("/obj/", StringComparison.Ordinal) && !f.Path.Contains("/bin/", StringComparison.Ordinal))
            .ToList();
        files.Should().Contain(f => f.Path == AllowedFile, "the allowed file must exist where this test expects it");
        files.Should().Contain(f => f.Path == CustomSqlModelFile, "the exempt file must exist where this test expects it");

        Offenders(files).Should().BeEmpty(
            "only {0} may name ConditionalType.In, ConditionalType.NotIn or ConditionalType.Equal, alias or import the enum, " +
            "or build a ConditionalModel without a ConditionalType; every other site builds its condition through it", AllowedFile);
    }

    [Theory]
    [InlineData("x = ConditionalType.In;", true)]
    [InlineData("x = ConditionalType . NotIn;", true)]
    [InlineData("// ConditionalType.In is the typed primitive", true)]
    [InlineData("/// <see cref=\"ConditionalType.NotIn\"/>", true)]
    [InlineData("x = ConditionalType.Equal;", true)]
    [InlineData("// ConditionalType.Equal reads the word null as a null test", true)]
    [InlineData("x = ConditionalType.EqualNull;", false)]
    [InlineData("x = ConditionalType.NoEqual;", false)]
    [InlineData("x = ConditionalType.IsNot;", false)]
    [InlineData("x = ConditionalType.InSingle;", false)]
    [InlineData("using static SqlSugar.ConditionalType;", true)]
    [InlineData("using static ConditionalType;", true)]
    [InlineData("using CT = SqlSugar.ConditionalType;", true)]
    [InlineData("using Kind = ConditionalType;", true)]
    [InlineData("using SqlSugar;", false)]
    [InlineData("var m = new ConditionalModel { FieldName = c, FieldValue = v };", true)]
    [InlineData("var m = new ConditionalModel() { FieldName = c, FieldValue = v };", true)]
    [InlineData("var m = new ConditionalModel { FieldName = c, CustomConditionalFunc = f };", true)]
    [InlineData("var m = new ConditionalModel { FieldName = c, ConditionalType = ConditionalType.IsNot };", false)]
    [InlineData("var m = new ConditionalModel { FieldName = c, ConditionalType = kind };", false)]
    [InlineData("var m = new ConditionalModel();", false)]
    public void The_scan_reads_code_and_comments_and_nothing_else(string text, bool flagged)
    {
        Offenders([("another file", text)]).Count.Should().Be(flagged ? 1 : 0);
        Offenders([(AllowedFile, text)]).Should().BeEmpty();
    }

    [Fact]
    public void A_model_that_renders_its_own_sql_is_exempt_by_file_and_by_nothing_else()
    {
        const string text = "var m = new ConditionalModel { FieldName = c, CustomConditionalFunc = f };";
        Offenders([(CustomSqlModelFile, text)]).Should().BeEmpty();
        Offenders([("another file", text)]).Should().ContainSingle();
        Offenders([(CustomSqlModelFile, "x = ConditionalType.Equal;")]).Should().ContainSingle();
    }
}
