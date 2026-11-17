using AwesomeAssertions;
using SqlSugar;
using Struo.Application.Configuration;
using Struo.Domain.Metadata.Attributes;
using Struo.Infrastructure.Metadata;
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
        Utf8Mb4Columns.Findings(table, live.Select(l => new ColumnCharacterSet(l.Column, l.CharacterSet, null)).ToList()).ToList();

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
    [InlineData("utf8mb3")]
    [InlineData("utf8")]
    public void The_utf8mb3_message_names_the_set_the_emoji_consequence_and_the_conversion_of_the_table(string characterSet)
    {
        var message = Check(Table(("title", ColumnCategory.String)), ("title", characterSet)).Single().Message;

        message.Should().Contain($"character set {characterSet}")
            .And.Contain("characters outside the Basic Multilingual Plane, such as emoji, cannot be stored or compared")
            .And.Contain("ALTER TABLE `t_docs` CONVERT TO CHARACTER SET utf8mb4");
    }

    [Fact]
    public void Another_character_set_message_says_what_that_set_cannot_store_and_names_the_conversion()
    {
        var message = Check(Table(("title", ColumnCategory.String)), ("title", "latin1")).Single().Message;

        message.Should().Contain("character set latin1")
            .And.Contain("characters that latin1 does not contain cannot be stored")
            .And.Contain("ALTER TABLE `t_docs` CONVERT TO CHARACTER SET utf8mb4")
            .And.NotContain("emoji");
    }

    [Theory]
    [InlineData(StruoDbType.MySql, true)]
    [InlineData(StruoDbType.Sqlite, false)]
    [InlineData(StruoDbType.PostgreSQL, false)]
    [InlineData(StruoDbType.SqlServer, false)]
    [InlineData(StruoDbType.Oracle, false)]
    public void Only_MySql_reads_the_character_sets(StruoDbType dbType, bool reads)
    {
        var calls = 0;
        IReadOnlyList<ColumnCharacterSet> Read()
        {
            calls++;
            return [new ColumnCharacterSet("title", "utf8mb3", "utf8mb3_general_ci")];
        }

        var findings = Utf8Mb4Columns.FindingsFor(dbType, Table(("title", ColumnCategory.String)), Read).ToList();

        calls.Should().Be(reads ? 1 : 0);
        findings.Should().HaveCount(reads ? 1 : 0);
    }

    [Fact]
    public void Every_StruoDbType_value_is_covered_by_the_backend_gate_theory() =>
        Enum.GetValues<StruoDbType>().Should().BeEquivalentTo(
            [StruoDbType.MySql, StruoDbType.Sqlite, StruoDbType.PostgreSQL, StruoDbType.SqlServer, StruoDbType.Oracle]);

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

    [Fact]
    public void The_catalog_read_model_is_not_a_table_or_a_collection()
    {
        var model = typeof(CharacterSetCatalog.InformationSchemaColumn);

        model.GetCustomAttributes(typeof(SugarTable), inherit: true).Should().BeEmpty();
        model.GetCustomAttributes(typeof(CmsCollectionAttribute), inherit: true).Should().BeEmpty();
        FrameworkEntityTypes.All.Should().NotContain(model);
    }
}
