using AwesomeAssertions;
using SqlSugar;
using Struo.Application.Configuration;
using Struo.Domain.Metadata.Attributes;
using Struo.Domain.Metadata.Enums;
using Struo.Infrastructure.Persistence;
using Struo.Tests.Support;
using Xunit;

namespace Struo.Tests.Persistence;

/// <summary>
/// Covers the SqlSugarClientFactory EntityService convention that widens content-bearing
/// [CmsField] interfaces (RichText/Textarea/Markdown/Code/Json) to a `text` column, so a
/// realistic body (tables, styled spans, multiple paragraphs) never hits Postgres's default
/// varchar(255) CodeFirst mapping (phase7g live gate: Npgsql 22001 "value too long").
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

    private static (SqliteTestDatabase, ISqlSugarClient) NewClient()
    {
        var db = new SqliteTestDatabase();
        var client = SqlSugarClientFactory.Create(
            new DatabaseOptions { DbType = StruoDbType.Sqlite, ConnectionString = db.ConnectionString },
            new TestCurrentUserAccessor(Guid.Empty));
        return (db, client);
    }

    [Fact]
    public void RichText_interface_column_is_text_plain_text_interface_keeps_default()
    {
        var (db, client) = NewClient();
        using (db)
        {
            client.CodeFirst.InitTables<ContentColumnTestEntity>();

            var columns = client.DbMaintenance.GetColumnInfosByTableName("content_column_test_entity", false);
            var body = columns.Single(c => c.DbColumnName.Equals("Body", StringComparison.OrdinalIgnoreCase));
            var plainText = columns.Single(c => c.DbColumnName.Equals("PlainText", StringComparison.OrdinalIgnoreCase));

            body.DataType.Should().ContainEquivalentOf("text");
            // Control: the default Text interface must NOT get the "text" override — it keeps
            // whatever SqlSugar's default CodeFirst mapping produces for a plain string on this
            // provider (varchar-family on SQLite's declared type affinity).
            plainText.DataType.Should().NotBeEquivalentTo(body.DataType);
            plainText.DataType.Should().ContainEquivalentOf("varchar");
        }
    }
}
