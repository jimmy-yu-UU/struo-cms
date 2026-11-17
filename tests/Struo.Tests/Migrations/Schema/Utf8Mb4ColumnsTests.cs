using AwesomeAssertions;
using Struo.Infrastructure.Migrations.Schema;
using Xunit;

namespace Struo.Tests.Migrations.Schema;

public sealed class Utf8Mb4ColumnsTests
{
    private static ExpectedTable Table(params (string Name, ColumnCategory Category)[] columns) =>
        new(typeof(object), "t_docs", "docs", true,
            columns.Select(c => new ExpectedColumn(c.Name, c.Category, true, null, false, false, false)).ToList(),
            [], null);

    private static List<SchemaFinding> Check(ExpectedTable table, params (string Column, string? CharacterSet)[] live) =>
        Utf8Mb4Columns.Findings(table, live.Select(l => new ColumnCharacterSet(l.Column, l.CharacterSet)).ToList()).ToList();

    [Theory]
    [InlineData(ColumnCategory.String, "utf8mb3")]
    [InlineData(ColumnCategory.String, "utf8")]
    [InlineData(ColumnCategory.String, "latin1")]
    [InlineData(ColumnCategory.LongText, "utf8mb3")]
    public void A_text_column_outside_utf8mb4_is_one_warning_naming_the_character_set(ColumnCategory category, string characterSet)
    {
        var findings = Check(Table(("title", category)), ("title", characterSet));

        var finding = findings.Should().ContainSingle().Subject;
        finding.Severity.Should().Be(FindingSeverity.Warning);
        finding.Kind.Should().Be(FindingKind.NonUtf8Mb4String);
        (finding.Table, finding.Column).Should().Be(("t_docs", "title"));
        finding.Message.Should().Contain(characterSet).And.Contain("utf8mb4");
    }

    [Theory]
    [InlineData("utf8mb4")]
    [InlineData("UTF8MB4")]
    [InlineData(null)]
    public void A_utf8mb4_column_or_one_without_a_character_set_reports_nothing(string? characterSet) =>
        Check(Table(("title", ColumnCategory.String)), ("title", characterSet)).Should().BeEmpty();

    [Theory]
    [InlineData(ColumnCategory.Guid)]
    [InlineData(ColumnCategory.Integer)]
    [InlineData(ColumnCategory.Other)]
    public void Only_string_and_long_text_columns_are_checked(ColumnCategory category) =>
        Check(Table(("id", category)), ("id", "utf8mb3")).Should().BeEmpty();

    [Fact]
    public void Columns_are_matched_by_lower_case_name_and_an_absent_column_is_skipped()
    {
        var table = Table(("title", ColumnCategory.String), ("missing", ColumnCategory.String));

        var findings = Check(table, ("Title", "utf8mb3"), ("undeclared", "utf8mb3"));

        findings.Should().ContainSingle().Which.Column.Should().Be("title");
    }

    [Fact]
    public void The_finding_never_makes_a_report_fail()
    {
        var findings = Check(Table(("title", ColumnCategory.String)), ("title", "utf8mb3"));

        new SchemaCheckReport(findings).HasErrors.Should().BeFalse();
    }
}
