using AwesomeAssertions;
using SqlSugar;
using Struo.Application.Configuration;
using Struo.Domain.Metadata.Attributes;
using Struo.Domain.Metadata.Enums;
using Struo.Infrastructure.Migrations.Schema;
using Struo.Infrastructure.Persistence;
using Struo.Tests.Support;
using Xunit;

namespace Struo.Tests.Persistence;

// A [CmsField] property of a structured type (a list, an array, a dictionary or a custom class) is
// stored as a JSON long-text column on Hidden and on the multi-value interfaces, with or without
// [SugarColumn(IsJson = true)]. Navigation and translation lists carry no [CmsField] and have no
// column.
public sealed class StructuredFieldColumnTests
{
    public sealed class ListFieldItem
    {
        public string A { get; set; } = "";
        public int B { get; set; }
    }

    [SugarTable("list_field_probe")]
    public sealed class ListFieldProbe
    {
        [SugarColumn(IsPrimaryKey = true, IsIdentity = true)] public long Id { get; set; }
        [CmsField(Label = "Names", Interface = FieldInterface.Hidden)] public List<string> Names { get; set; } = [];
        [CmsField(Label = "Items", Interface = FieldInterface.Hidden)] public List<ListFieldItem> Items { get; set; } = [];
        [CmsField(Label = "Array", Interface = FieldInterface.Hidden)] public string[] Array { get; set; } = [];
        [CmsField(Label = "Map", Interface = FieldInterface.Hidden)] public Dictionary<string, string> Map { get; set; } = new();
        [CmsField(Label = "Custom", Interface = FieldInterface.Hidden)] public ListFieldItem Custom { get; set; } = new();
        [CmsField(Label = "Marked", Interface = FieldInterface.Tags)] public List<string> Marked { get; set; } = [];
        [SugarColumn(IsJson = true)]
        [CmsField(Label = "Annotated", Interface = FieldInterface.Hidden)] public List<string> Annotated { get; set; } = [];
        [SugarColumn(IsNullable = true)]
        [CmsField(Label = "Maybe", Interface = FieldInterface.Hidden)] public List<string>? Maybe { get; set; }
    }

    // An entity the scan never sees (a junction POCO that is not a collection) still gets the JSON
    // column for a structured [CmsField] property, whatever its interface.
    [SugarTable("list_field_unscanned_probe")]
    public sealed class UnscannedProbe
    {
        [SugarColumn(IsPrimaryKey = true, IsIdentity = true)] public long Id { get; set; }
        [CmsField(Label = "Names", Interface = FieldInterface.Text)] public List<string> Names { get; set; } = [];
    }

    [SugarTable("list_field_untouched_children")]
    public sealed class UntouchedChild
    {
        [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }
    }

    [SugarTable("list_field_untouched_links")]
    public sealed class UntouchedLink
    {
        [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }
        public Guid UntouchedParentId { get; set; }
        public Guid UntouchedChildId { get; set; }
    }

    public sealed class UntouchedTranslation
    {
        [SugarColumn(IsPrimaryKey = true)] public long Id { get; set; }
        public Guid UntouchedParentId { get; set; }
        public string Locale { get; set; } = "";
        [CmsField(Label = "Title", Interface = FieldInterface.Text)] public string Title { get; set; } = "";
    }

    [SugarTable("list_field_untouched_parents")]
    [CmsCollection("Untouched parent", Name = "lfUntouchedParent")]
    public sealed class UntouchedParent
    {
        [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }
        [CmsField(Label = "Name", Interface = FieldInterface.Text)] public string Name { get; set; } = "";

        [CmsTranslations(typeof(UntouchedTranslation))]
        [SugarColumn(IsIgnore = true)] public List<UntouchedTranslation> Translations { get; set; } = [];

        [Navigate(typeof(UntouchedLink), nameof(UntouchedLink.UntouchedParentId), nameof(UntouchedLink.UntouchedChildId))]
        [CmsRelation(Interface = RelationInterface.TagSelect)]
        [SugarColumn(IsIgnore = true)] public List<UntouchedChild> Children { get; set; } = [];
    }

    private static EntityInfo InfoOf<T>(DbType dbType) =>
        SqlSugarClientFactory.Create(
                new DatabaseOptions
                {
                    DbType = Enum.Parse<StruoDbType>(dbType.ToString()),
                    ConnectionString = "Data Source=unused",
                    TablePrefix = "lf" + Guid.NewGuid().ToString("N")[..8] + "_",
                },
                new TestCurrentUserAccessor(null))
            .EntityMaintenance.GetEntityInfo<T>();

    [Theory]
    [MemberData(nameof(ColumnTypeMapTests.CodeFirstBackends), MemberType = typeof(ColumnTypeMapTests))]
    public void A_structured_CmsField_is_a_JSON_long_text_column(DbType dbType)
    {
        var columns = InfoOf<ListFieldProbe>(dbType).Columns;

        foreach (var name in new[] { "Names", "Items", "Array", "Map", "Custom", "Marked", "Annotated", "Maybe" })
        {
            var column = columns.Single(c => c.PropertyName == name);
            column.IsJson.Should().BeTrue(name);
            column.DataType.Should().Be(ColumnTypeMap.For(ColumnShape.LongText, dbType), name);
        }
        columns.Single(c => c.PropertyName == "Maybe").IsNullable.Should().BeTrue();
    }

    [Theory]
    [MemberData(nameof(ColumnTypeMapTests.CodeFirstBackends), MemberType = typeof(ColumnTypeMapTests))]
    public void An_unscanned_entity_gets_the_JSON_column_for_a_structured_CmsField(DbType dbType)
    {
        var column = InfoOf<UnscannedProbe>(dbType).Columns.Single(c => c.PropertyName == "Names");

        column.IsJson.Should().BeTrue();
        column.DataType.Should().Be(ColumnTypeMap.For(ColumnShape.LongText, dbType));
    }

    [Theory]
    [MemberData(nameof(ColumnTypeMapTests.CodeFirstBackends), MemberType = typeof(ColumnTypeMapTests))]
    public void Navigation_and_translation_lists_have_no_column_and_no_JSON_mapping(DbType dbType)
    {
        var columns = InfoOf<UntouchedParent>(dbType).Columns;

        foreach (var name in new[] { nameof(UntouchedParent.Translations), nameof(UntouchedParent.Children) })
        {
            var column = columns.Single(c => c.PropertyName == name);
            column.IsIgnore.Should().BeTrue(name);
            column.IsJson.Should().BeFalse(name);
        }
    }

    [Fact]
    public void An_untouched_parent_still_builds_its_table_and_stores_a_row()
    {
        using var db = TestBackend.CreateUnitDatabase();
        var client = SqlSugarClientFactory.Create(db.Options(), new TestCurrentUserAccessor(Guid.Empty));
        client.CodeFirst.InitTables<UntouchedParent>();

        var id = Guid.NewGuid();
        client.Insertable(new UntouchedParent { Id = id, Name = "p" }).ExecuteCommand();

        client.Queryable<UntouchedParent>().Single(p => p.Id == id).Name.Should().Be("p");
        client.DbMaintenance.GetColumnInfosByTableName("list_field_untouched_parents", false)
            .Select(c => c.DbColumnName.ToLowerInvariant()).Should().BeEquivalentTo("id", "name");
    }

    [Fact]
    public void Migration_source_for_a_structured_CmsField_uses_AsJson_for_every_structured_property()
    {
        var client = SqlSugarClientFactory.Create(
            new DatabaseOptions
            {
                DbType = StruoDbType.Sqlite,
                ConnectionString = "Data Source=unused",
                TablePrefix = "lm" + Guid.NewGuid().ToString("N")[..8] + "_",
            },
            new TestCurrentUserAccessor(null));

        var source = MigrationGenerator.Generate(
            new MigrationSpec("AddListFieldProbe", 202611130000, null, EntitySchemaReader.Read(client, typeof(ListFieldProbe))));

        foreach (var name in new[] { "Names", "Items", "Array", "Map", "Custom", "Marked", "Annotated" })
            source.Should().Contain($".WithColumn(\"{name.ToLowerInvariant()}\").AsJson(Db).NotNullable()");
        source.Should().Contain(".WithColumn(\"maybe\").AsJson(Db).Nullable()");
        source.Should().NotContain("AsString(1)").And.NotContain("AsLongText(Db)");
    }

    public static TheoryData<int> ElementCounts => new(0, 1, 3);

    [Theory]
    [MemberData(nameof(ElementCounts))]
    public void A_structured_CmsField_without_IsJson_reads_back_what_was_written(int count)
    {
        using var db = TestBackend.CreateUnitDatabase();
        var client = SqlSugarClientFactory.Create(db.Options(), new TestCurrentUserAccessor(Guid.Empty));
        client.CodeFirst.InitTables<ListFieldProbe>();
        var names = Enumerable.Range(0, count).Select(i => $"n{i}").ToList();
        var items = Enumerable.Range(0, count).Select(i => new ListFieldItem { A = $"a{i}", B = i }).ToList();

        var map = names.ToDictionary(n => n, n => n.ToUpperInvariant());
        var custom = new ListFieldItem { A = "c" + count, B = count };

        var id = client.Insertable(new ListFieldProbe
            {
                Names = names, Items = items, Array = names.ToArray(), Map = map, Custom = custom, Maybe = names,
            })
            .ExecuteReturnBigIdentity();

        var read = client.Queryable<ListFieldProbe>().InSingle(id);
        read.Names.Should().Equal(names);
        read.Items.Should().BeEquivalentTo(items, o => o.WithStrictOrdering());
        read.Array.Should().Equal(names);
        read.Map.Should().Equal(map);
        read.Custom.Should().BeEquivalentTo(custom);
        read.Maybe.Should().Equal(names);
    }

    [Fact]
    public void A_structured_CmsField_without_IsJson_can_be_updated_and_cleared()
    {
        using var db = TestBackend.CreateUnitDatabase();
        var client = SqlSugarClientFactory.Create(db.Options(), new TestCurrentUserAccessor(Guid.Empty));
        client.CodeFirst.InitTables<ListFieldProbe>();
        var id = client.Insertable(new ListFieldProbe { Names = ["x"], Items = [new ListFieldItem { A = "x", B = 1 }] })
            .ExecuteReturnBigIdentity();

        var row = client.Queryable<ListFieldProbe>().InSingle(id);
        row.Names = ["p", "q", "r"];
        row.Array = ["x", "y"];
        row.Map = new Dictionary<string, string> { ["k"] = "v" };
        row.Custom = new ListFieldItem { A = "z", B = 9 };
        row.Items = [];
        row.Maybe = null;
        client.Updateable(row).ExecuteCommand();

        var read = client.Queryable<ListFieldProbe>().InSingle(id);
        read.Names.Should().Equal("p", "q", "r");
        read.Array.Should().Equal("x", "y");
        read.Map.Should().Equal(new Dictionary<string, string> { ["k"] = "v" });
        read.Custom.Should().BeEquivalentTo(new ListFieldItem { A = "z", B = 9 });
        read.Items.Should().BeEmpty();
        read.Maybe.Should().BeNull();
    }
}
