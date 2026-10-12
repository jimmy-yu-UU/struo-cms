using AwesomeAssertions;
using SqlSugar;
using Struo.Domain.Metadata.Attributes;
using Struo.Domain.Metadata.Enums;
using Struo.Domain.Metadata.Models;
using Struo.Infrastructure.Persistence;
using Struo.Tests.Support;
using Xunit;

namespace Struo.Tests.Persistence;

/// <summary>
/// The SqlSugarClientFactory convention maps multi-value [CmsField] interfaces
/// (MultiSelect/CheckboxGroup/Tags) to an `IsJson` + LongText column so a List&lt;string&gt; /
/// List&lt;TagItem&gt; round-trips. The column type MUST be widened: `IsJson` alone leaves the length
/// unset and Postgres defaults it to `varchar(1)`, truncating any real JSON payload. SQLite ignores
/// declared length, so the type assertion is what pins this on a SQLite run.
/// </summary>
public class MultiValueColumnMappingTests
{
    [SugarTable("multivalue_col_test_entity")]
    private sealed class MvColTestEntity
    {
        [SugarColumn(IsPrimaryKey = true, IsIdentity = true)]
        public long Id { get; set; }

        [CmsField(Label = "Regions", Interface = FieldInterface.MultiSelect)]
        public List<string> Regions { get; set; } = [];

        [CmsField(Label = "Keywords", Interface = FieldInterface.Tags)]
        public List<TagItem> Keywords { get; set; } = [];
    }

    [Fact]
    public void Multi_value_columns_are_widened_to_LongText()
    {
        using var db = TestBackend.CreateUnitDatabase();
        var client = SqlSugarClientFactory.Create(db.Options(), new TestCurrentUserAccessor(Guid.Empty));
        client.CodeFirst.InitTables<MvColTestEntity>();

        var columns = client.DbMaintenance.GetColumnInfosByTableName("multivalue_col_test_entity", false);
        foreach (var col in new[] { "Regions", "Keywords" })
            columns.Single(c => c.DbColumnName.Equals(col, StringComparison.OrdinalIgnoreCase))
                .ShouldHaveShape(ColumnShape.LongText, db.DbType);
    }

    [Fact]
    public void Multi_value_lists_round_trip_through_a_json_column()
    {
        using var db = TestBackend.CreateUnitDatabase();
        var client = SqlSugarClientFactory.Create(db.Options(), new TestCurrentUserAccessor(Guid.Empty));
        client.CodeFirst.InitTables<MvColTestEntity>();

        var row = new MvColTestEntity
        {
            Regions = ["apac", "emea"],
            Keywords = [new TagItem("tech"), new TagItem("ai", "人工智慧")],
        };
        client.Insertable(row).ExecuteCommand();

        var read = client.Queryable<MvColTestEntity>().First();
        read.Regions.Should().Equal("apac", "emea");
        read.Keywords.Should().Equal(new TagItem("tech"), new TagItem("ai", "人工智慧"));
    }
}
