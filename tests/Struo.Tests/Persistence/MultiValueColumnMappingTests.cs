using AwesomeAssertions;
using SqlSugar;
using Struo.Application.Configuration;
using Struo.Domain.Metadata.Attributes;
using Struo.Domain.Metadata.Enums;
using Struo.Domain.Metadata.Models;
using Struo.Infrastructure.Persistence;
using Struo.Tests.Support;
using Xunit;

namespace Struo.Tests.Persistence;

/// <summary>
/// The SqlSugarClientFactory convention maps multi-value [CmsField] interfaces
/// (MultiSelect/CheckboxGroup/Tags) to a JSON column so a List&lt;string&gt; / List&lt;TagItem&gt;
/// round-trips. Verified cross-db via an actual insert+read (jsonb on PG, JSON-in-text on SQLite).
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
    public void Multi_value_lists_round_trip_through_a_json_column()
    {
        var db = new SqliteTestDatabase();
        var client = SqlSugarClientFactory.Create(
            new DatabaseOptions { DbType = StruoDbType.Sqlite, ConnectionString = db.ConnectionString },
            new TestCurrentUserAccessor(Guid.Empty));
        using (db)
        {
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
}
