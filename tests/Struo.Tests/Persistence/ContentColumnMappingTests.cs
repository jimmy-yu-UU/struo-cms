using AwesomeAssertions;
using SqlSugar;
using Struo.Domain.Metadata.Attributes;
using Struo.Domain.Metadata.Enums;
using Struo.Infrastructure.Persistence;
using Struo.Tests.Support;
using Xunit;

namespace Struo.Tests.Persistence;

/// <summary>
/// Covers the SqlSugarClientFactory EntityService convention that widens content-bearing
/// [CmsField] interfaces (RichText/Textarea/Markdown/Code/Json) to a LongText column, so a
/// realistic body (tables, styled spans, multiple paragraphs) never hits Postgres's default
/// varchar(255) CodeFirst mapping (live-PostgreSQL gate: Npgsql 22001 "value too long").
/// </summary>
public class ContentColumnMappingTests
{
    [SugarTable("content_column_test_entity")]
    private sealed class ContentColumnTestEntity
    {
        [SugarColumn(IsPrimaryKey = true, IsIdentity = true)]
        public long Id { get; set; }

        [SugarColumn(IsNullable = true)]
        [CmsField(Label = "Body", Interface = FieldInterface.RichText)]
        public string? Body { get; set; }

        // Plain Text interface (the CmsFieldAttribute default) must NOT be widened — MaxLength
        // support for Text fields is a separate feature.
        [SugarColumn(IsNullable = true)]
        [CmsField(Label = "PlainText", Interface = FieldInterface.Text)]
        public string? PlainText { get; set; }
    }

    [Fact]
    public void RichText_interface_column_is_LongText_plain_text_interface_keeps_default()
    {
        using var db = TestBackend.CreateUnitDatabase();
        var client = SqlSugarClientFactory.Create(db.Options(), new TestCurrentUserAccessor(Guid.Empty));
        client.CodeFirst.InitTables<ContentColumnTestEntity>();

        var columns = client.DbMaintenance.GetColumnInfosByTableName("content_column_test_entity", false);
        var body = columns.Single(c => c.DbColumnName.Equals("Body", StringComparison.OrdinalIgnoreCase));
        var plainText = columns.Single(c => c.DbColumnName.Equals("PlainText", StringComparison.OrdinalIgnoreCase));

        body.ShouldHaveShape(ColumnShape.LongText, db.DbType);
        // Control: the default Text interface must NOT get the LongText override; it keeps SqlSugar's
        // default CodeFirst mapping for a plain string (varchar-family). SQL Server reports nvarchar for
        // both, so the length is what tells them apart there.
        (plainText.DataType, plainText.Length).Should().NotBe((body.DataType, body.Length));
        plainText.DataType.Should().ContainEquivalentOf("varchar");
    }
}
