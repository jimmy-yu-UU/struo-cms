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
/// it catches a mention in code or in a comment, a <c>using static</c> of the enum, an alias of it (each also
/// with the <c>SqlSugar.</c> or <c>global::</c> qualifier), <c>default</c> as the condition type, a verbatim
/// <c>@Equal</c>, and a <c>ConditionalModel</c> object initializer, typed or target-typed, that names no
/// <c>ConditionalType</c> (the default is <c>Equal</c>). It cannot see what text does not show: a cast from a
/// number, a condition type chosen through reflection or <c>Enum.Parse</c>, a model whose
/// <c>ConditionalType</c> is assigned in a separate statement, or a model built with <c>new ConditionalModel()</c>
/// and filled in afterwards.
/// </summary>
public sealed partial class ValueConditionsConventionTests
{
    private const string AllowedFile = "src/Struo.Infrastructure/Query/ValueConditions.cs";

    // Models that set CustomConditionalFunc. The ORM reads that function before ConditionalType, so these
    // models render their own SQL and their condition type takes no part. They are exempt from the
    // initializer rule only.
    private static readonly string[] CustomSqlModelFiles =
    [
        "src/Struo.Infrastructure/Query/SubQueryConditional.cs",
        "src/Struo.Infrastructure/Query/OrOfSubqueriesConditional.cs",
    ];

    // Each pattern allows the qualifiers SqlSugar. and global::SqlSugar.
    private const string EnumName = @"(?:global\s*::\s*)?(?:SqlSugar\s*\.\s*)?ConditionalType";

    [GeneratedRegex(EnumName + @"\s*\.\s*@?(In|NotIn|Equal)\b")]
    private static partial Regex SetMember();

    [GeneratedRegex(@"using\s+static\s+" + EnumName + @"\s*;")]
    private static partial Regex UsingStatic();

    [GeneratedRegex(@"using\s+\w+\s*=\s*" + EnumName + @"\s*;")]
    private static partial Regex Alias();

    [GeneratedRegex(@"\bConditionalType\s*=\s*default\b|\bdefault\s*\(\s*" + EnumName + @"\s*\)")]
    private static partial Regex DefaultType();

    // new ConditionalModel { ... }, new SqlSugar.ConditionalModel() { ... } and the target-typed new() { ... }.
    [GeneratedRegex(@"\bnew\s*(?<type>(?:global\s*::\s*)?(?:SqlSugar\s*\.\s*)?ConditionalModel)?\s*(?:\(\s*\))?\s*\{")]
    private static partial Regex Initializer();

    private static string BodyAfter(string text, Match match)
    {
        var start = match.Index + match.Length;
        var depth = 1;
        var end = start;
        while (end < text.Length && depth > 0)
        {
            if (text[end] == '{') depth++;
            else if (text[end] == '}') depth--;
            end++;
        }
        return text[start..end];
    }

    // A typed initializer is a model whatever it sets; a target-typed one is read as a model when it sets
    // FieldName or FieldValue. Either way it must name ConditionalType.
    private static bool HasInitializerWithoutType(string text)
    {
        foreach (Match match in Initializer().Matches(text))
        {
            var body = BodyAfter(text, match);
            var isModel = match.Groups["type"].Success || body.Contains("FieldName", StringComparison.Ordinal)
                                                        || body.Contains("FieldValue", StringComparison.Ordinal);
            if (isModel && !body.Contains("ConditionalType", StringComparison.Ordinal)) return true;
        }
        return false;
    }

    private static bool NamesTheEnumInAForbiddenWay(string text) =>
        SetMember().IsMatch(text) || UsingStatic().IsMatch(text) || Alias().IsMatch(text) || DefaultType().IsMatch(text);

    internal static IReadOnlyList<string> Offenders(IEnumerable<(string Path, string Text)> files) =>
        files.Where(f => f.Path != AllowedFile
                         && (NamesTheEnumInAForbiddenWay(f.Text)
                             || (!CustomSqlModelFiles.Contains(f.Path) && HasInitializerWithoutType(f.Text))))
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
        foreach (var exempt in CustomSqlModelFiles)
            files.Should().Contain(f => f.Path == exempt, "the exempt file must exist where this test expects it");

        Offenders(files).Should().BeEmpty(
            "only {0} may name ConditionalType.In, ConditionalType.NotIn or ConditionalType.Equal, import, alias or default " +
            "the enum, or build a ConditionalModel without a ConditionalType; every other site builds its condition through it",
            AllowedFile);
    }

    [Theory]
    [InlineData("x = ConditionalType.In;", true)]
    [InlineData("x = ConditionalType . NotIn;", true)]
    [InlineData("// ConditionalType.In is the typed primitive", true)]
    [InlineData("/// <see cref=\"ConditionalType.NotIn\"/>", true)]
    [InlineData("x = ConditionalType.Equal;", true)]
    [InlineData("x = ConditionalType.@Equal;", true)]
    [InlineData("x = SqlSugar.ConditionalType.Equal;", true)]
    [InlineData("x = global::SqlSugar.ConditionalType.In;", true)]
    [InlineData("// ConditionalType.Equal reads the word null as a null test", true)]
    [InlineData("x = ConditionalType.EqualNull;", false)]
    [InlineData("x = ConditionalType.NoEqual;", false)]
    [InlineData("x = ConditionalType.IsNot;", false)]
    [InlineData("x = ConditionalType.InSingle;", false)]
    [InlineData("using static SqlSugar.ConditionalType;", true)]
    [InlineData("using static ConditionalType;", true)]
    [InlineData("using static global::SqlSugar.ConditionalType;", true)]
    [InlineData("using CT = SqlSugar.ConditionalType;", true)]
    [InlineData("using CT = global::SqlSugar.ConditionalType;", true)]
    [InlineData("using Kind = ConditionalType;", true)]
    [InlineData("using SqlSugar;", false)]
    [InlineData("var m = new ConditionalModel { FieldName = c, ConditionalType = default };", true)]
    [InlineData("m.ConditionalType = default;", true)]
    [InlineData("var t = default(ConditionalType);", true)]
    [InlineData("var t = default(SqlSugar.ConditionalType);", true)]
    [InlineData("var m = new ConditionalModel { FieldName = c, FieldValue = v };", true)]
    [InlineData("var m = new ConditionalModel() { FieldName = c, FieldValue = v };", true)]
    [InlineData("var m = new SqlSugar.ConditionalModel { FieldName = c, FieldValue = v };", true)]
    [InlineData("var m = new global::SqlSugar.ConditionalModel { FieldName = c };", true)]
    [InlineData("ConditionalModel m = new() { FieldName = c, FieldValue = v };", true)]
    [InlineData("ConditionalModel m = new () { FieldName = c };", true)]
    [InlineData("static ConditionalModel Make(string c) => new() { FieldName = c, FieldValue = \"x\" };", true)]
    [InlineData("var m = new ConditionalModel { FieldName = c, CustomConditionalFunc = f };", true)]
    [InlineData("var m = new ConditionalModel { FieldName = c, ConditionalType = ConditionalType.IsNot };", false)]
    [InlineData("var m = new ConditionalModel { FieldName = c, ConditionalType = kind };", false)]
    [InlineData("ConditionalModel m = new() { FieldName = c, ConditionalType = kind };", false)]
    [InlineData("var m = new ConditionalModel();", false)]
    [InlineData("var list = new() { 1, 2 };", false)]
    [InlineData("var o = new Options { FieldName = c };", false)]
    [InlineData("List<string> l = new() { \"a\" };", false)]
    public void The_scan_reads_code_and_comments_and_nothing_else(string text, bool flagged)
    {
        Offenders([("another file", text)]).Count.Should().Be(flagged ? 1 : 0);
        Offenders([(AllowedFile, text)]).Should().BeEmpty();
    }

    [Fact]
    public void A_model_that_renders_its_own_sql_is_exempt_by_file_from_the_initializer_rule_only()
    {
        const string typed = "var m = new ConditionalModel { FieldName = c, CustomConditionalFunc = f };";
        const string targetTyped = "static ConditionalModel Wrap(string c) => new() { FieldName = c, CustomConditionalFunc = f };";
        foreach (var file in CustomSqlModelFiles)
        {
            Offenders([(file, typed)]).Should().BeEmpty();
            Offenders([(file, targetTyped)]).Should().BeEmpty();
            Offenders([(file, "x = ConditionalType.Equal;")]).Should().ContainSingle();
            Offenders([(file, "using static SqlSugar.ConditionalType;")]).Should().ContainSingle();
        }
        Offenders([("another file", typed)]).Should().ContainSingle();
        Offenders([("another file", targetTyped)]).Should().ContainSingle();
    }
}
