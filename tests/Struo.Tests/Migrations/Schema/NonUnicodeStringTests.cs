using AwesomeAssertions;
using SqlSugar;
using Struo.Application.Configuration;
using Struo.Infrastructure.Migrations.Schema;
using Xunit;

namespace Struo.Tests.Migrations.Schema;

public sealed class NonUnicodeStringTests
{
    private static ExpectedTable Table(ColumnCategory category)
    {
        var column = new ExpectedColumn("title", category, false, category == ColumnCategory.String ? 255 : null, false, false, false);
        return new ExpectedTable(typeof(object), "t_docs", "docs", true, [column], [], null);
    }

    private static List<SchemaFinding> Compare(StruoDbType db, ColumnCategory category, string liveType, int length)
    {
        var table = Table(category);
        var live = new DbColumnInfo { DbColumnName = "title", DataType = liveType, Length = length, IsNullable = false };
        return SchemaTableComparer.CompareColumn(db, table, table.Columns[0], live).ToList();
    }

    [Theory]
    [InlineData(ColumnCategory.String, "varchar", 255)]
    [InlineData(ColumnCategory.String, "char", 255)]
    [InlineData(ColumnCategory.LongText, "varchar", -1)]
    [InlineData(ColumnCategory.LongText, "text", -1)]
    public void A_non_unicode_string_column_on_SQL_Server_is_one_warning(ColumnCategory category, string liveType, int length)
    {
        var findings = Compare(StruoDbType.SqlServer, category, liveType, length);

        var finding = findings.Should().ContainSingle().Subject;
        finding.Kind.Should().Be(FindingKind.NonUnicodeString);
        finding.Severity.Should().Be(FindingSeverity.Warning);
        finding.Table.Should().Be("t_docs");
        finding.Column.Should().Be("title");
        finding.Message.Should().Contain(liveType);
    }

    [Theory]
    [InlineData(StruoDbType.SqlServer, ColumnCategory.String, "nvarchar", 255)]
    [InlineData(StruoDbType.SqlServer, ColumnCategory.LongText, "nvarchar", -1)]
    [InlineData(StruoDbType.PostgreSQL, ColumnCategory.String, "varchar", 255)]
    [InlineData(StruoDbType.MySql, ColumnCategory.String, "varchar", 255)]
    [InlineData(StruoDbType.Sqlite, ColumnCategory.String, "varchar", 255)]
    public void Unicode_columns_and_other_backends_report_nothing(StruoDbType db, ColumnCategory category, string liveType, int length) =>
        Compare(db, category, liveType, length).Should().BeEmpty();
}
