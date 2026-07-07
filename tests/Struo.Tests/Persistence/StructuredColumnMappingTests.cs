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
/// The SqlSugarClientFactory convention maps a KeyValue [CmsField] (Dictionary&lt;string,string&gt;)
/// to an `IsJson` + `text` column, and a Json [CmsField] (string?) to a plain nullable `text` column
/// (via the content-bearing convention). Both must be `text`, not the default varchar — `IsJson` alone
/// is `varchar(1)` on Postgres (slice-1 finding) and an undeclared string is `varchar(255)`. Verified
/// cross-db via an actual insert+read (SQLite reports the declared type, pinning the convention here).
/// </summary>
public class StructuredColumnMappingTests
{
    [SugarTable("structured_col_test_entity")]
    private sealed class StructColTestEntity
    {
        [SugarColumn(IsPrimaryKey = true, IsIdentity = true)]
        public long Id { get; set; }

        [CmsField(Label = "Meta", Interface = FieldInterface.KeyValue)]
        public Dictionary<string, string> Meta { get; set; } = new();

        [CmsField(Label = "Attributes", Interface = FieldInterface.Json)]
        public string? Attributes { get; set; }
    }

    [Fact]
    public void Structured_columns_are_text_and_round_trip()
    {
        var db = new SqliteTestDatabase();
        var client = SqlSugarClientFactory.Create(
            new DatabaseOptions { DbType = StruoDbType.Sqlite, ConnectionString = db.ConnectionString },
            new TestCurrentUserAccessor(Guid.Empty));
        using (db)
        {
            client.CodeFirst.InitTables<StructColTestEntity>();

            var columns = client.DbMaintenance.GetColumnInfosByTableName("structured_col_test_entity", false);
            foreach (var col in new[] { "Meta", "Attributes" })
            {
                var info = columns.Single(c => c.DbColumnName.Equals(col, StringComparison.OrdinalIgnoreCase));
                info.DataType.Should().ContainEquivalentOf("text");
            }

            var row = new StructColTestEntity
            {
                Meta = new Dictionary<string, string> { ["seo-title"] = "值", ["author"] = "me" },
                Attributes = """{"a":1,"nested":{"x":"人工智慧"},"arr":[1,2]}""",
            };
            client.Insertable(row).ExecuteCommand();

            var read = client.Queryable<StructColTestEntity>().First();
            read.Meta.Should().ContainKey("seo-title");
            read.Meta["seo-title"].Should().Be("值");
            read.Meta["author"].Should().Be("me");
            read.Attributes.Should().Be("""{"a":1,"nested":{"x":"人工智慧"},"arr":[1,2]}""");
        }
    }
}
