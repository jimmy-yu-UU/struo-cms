using AwesomeAssertions;
using SqlSugar;
using Struo.Application.Configuration;
using Struo.Infrastructure.Files;
using Struo.Infrastructure.Metadata;
using Struo.Infrastructure.Migrations.Schema;
using Struo.Infrastructure.Persistence;
using Struo.Infrastructure.Revisions;
using Struo.Tests.Support;
using Xunit;

namespace Struo.Tests.Migrations.Schema;

public class EntitySchemaReaderTests
{
    // SqlSugar caches entity info per ConfigId, which is derived from the prefix: tests that differ in
    // policy use their own prefix.
    private static ISqlSugarClient Client(TranslationSidecarIndexPolicy? policy = null, string prefix = "struo_") =>
        SqlSugarClientFactory.Create(
            new DatabaseOptions
            {
                DbType = StruoDbType.Sqlite,
                ConnectionString = "Data Source=:memory:",
                TablePrefix = prefix
            },
            new TestCurrentUserAccessor(Guid.Empty),
            policy);

    private static ExpectedColumn Col(ExpectedTable t, string name) => t.Columns.Single(c => c.Name == name);

    [Fact]
    public void Names_are_lower_case_and_ignored_columns_are_excluded()
    {
        var table = EntitySchemaReader.Read(Client(), typeof(SchemaProbe));

        table.PhysicalName.Should().Be("schema_probe");
        table.LogicalName.Should().Be("schema_probe");
        table.IsFrameworkTable.Should().BeFalse();
        table.TranslationLogicalTable.Should().BeNull();
        table.Columns.Select(c => c.Name).Should().BeEquivalentTo(
            ["id", "code", "displayname", "note", "body", "tags", "createdat", "stamp", "kind", "price", "amount", "ratio", "flag", "ref", "blob"]);
    }

    [Fact]
    public void Primary_key_and_identity_are_reported()
    {
        var id = Col(EntitySchemaReader.Read(Client(), typeof(SchemaProbe)), "id");

        id.IsPrimaryKey.Should().BeTrue();
        id.IsIdentity.Should().BeTrue();
        id.Category.Should().Be(ColumnCategory.BigInteger);
    }

    [Fact]
    public void String_length_defaults_to_255_and_explicit_length_is_kept()
    {
        var table = EntitySchemaReader.Read(Client(), typeof(SchemaProbe));

        Col(table, "code").Category.Should().Be(ColumnCategory.String);
        Col(table, "code").Length.Should().Be(255);
        Col(table, "displayname").Length.Should().Be(80);
        Col(table, "id").Length.Should().BeNull();
        Col(table, "price").Length.Should().BeNull();
    }

    [Fact]
    public void Nullability_follows_nullable_reference_types_and_default_is_not_null()
    {
        var table = EntitySchemaReader.Read(Client(), typeof(SchemaProbe));

        Col(table, "note").IsNullable.Should().BeTrue();
        Col(table, "code").IsNullable.Should().BeFalse();
    }

    [Fact]
    public void Decimal_precision_and_scale_are_kept_only_when_declared()
    {
        var table = EntitySchemaReader.Read(Client(), typeof(SchemaProbe));

        Col(table, "amount").Length.Should().Be(10);
        Col(table, "amount").Scale.Should().Be(2);
        Col(table, "price").Length.Should().BeNull();
        Col(table, "price").Scale.Should().BeNull();
        Col(table, "ratio").Length.Should().BeNull();
        Col(table, "ratio").Scale.Should().BeNull();
        Col(table, "code").Scale.Should().BeNull();
    }

    [Fact]
    public void Decimal_with_a_precision_and_zero_digits_has_scale_zero()
    {
        var whole = Col(EntitySchemaReader.Read(Client(prefix: "ex_"), typeof(ExplicitTypeProbe)), "whole");

        whole.Length.Should().Be(10);
        whole.Scale.Should().Be(0);
    }

    [Fact]
    public void An_explicit_column_data_type_the_reader_does_not_know_is_other()
    {
        var table = EntitySchemaReader.Read(Client(prefix: "ex_"), typeof(ExplicitTypeProbe));

        Col(table, "wide").Category.Should().Be(ColumnCategory.Other);
        Col(table, "wide").Length.Should().BeNull();
        Col(table, "id").Category.Should().Be(ColumnCategory.BigInteger);
    }

    [Fact]
    public void Shapes_json_and_clr_types_map_to_categories()
    {
        var table = EntitySchemaReader.Read(Client(), typeof(SchemaProbe));

        Col(table, "body").Category.Should().Be(ColumnCategory.LongText);
        Col(table, "body").Length.Should().BeNull();
        Col(table, "body").IsJson.Should().BeFalse();
        Col(table, "tags").Category.Should().Be(ColumnCategory.LongText);
        Col(table, "tags").IsJson.Should().BeTrue();
        Col(table, "createdat").Category.Should().Be(ColumnCategory.DateTimeWithTimeZone);
        Col(table, "stamp").Category.Should().Be(ColumnCategory.DateTimeWithTimeZone);
        Col(table, "kind").Category.Should().Be(ColumnCategory.Integer);
        Col(table, "price").Category.Should().Be(ColumnCategory.Decimal);
        Col(table, "amount").Category.Should().Be(ColumnCategory.Decimal);
        Col(table, "ratio").Category.Should().Be(ColumnCategory.Double);
        Col(table, "flag").Category.Should().Be(ColumnCategory.Boolean);
        Col(table, "ref").Category.Should().Be(ColumnCategory.Guid);
        Col(table, "blob").Category.Should().Be(ColumnCategory.Binary);
    }

    [Fact]
    public void SugarIndex_attributes_resolve_the_table_token_and_keep_declaration_order()
    {
        var table = EntitySchemaReader.Read(Client(), typeof(SchemaProbe));

        table.Indexes.Should().BeEquivalentTo(
        [
            new ExpectedIndex("ux_schema_probe_code", ["code"], true),
            new ExpectedIndex("ix_schema_probe_name_kind", ["displayname", "kind"], false),
        ], o => o.WithStrictOrdering());
    }

    [Fact]
    public void Framework_table_is_prefixed_and_keeps_its_unique_index()
    {
        var table = EntitySchemaReader.Read(Client(), typeof(Revision));

        table.IsFrameworkTable.Should().BeTrue();
        table.PhysicalName.Should().Be("struo_revisions");
        table.LogicalName.Should().Be("revisions");
        table.Indexes.Should().ContainSingle().Which.Should().BeEquivalentTo(
            new ExpectedIndex("ux_struo_revisions_item_no", ["collectionname", "itemid", "revisionnumber"], true),
            o => o.WithStrictOrdering());
        Col(table, "snapshot").Category.Should().Be(ColumnCategory.LongText);
    }

    [Fact]
    public void Prefix_comes_from_the_client()
    {
        EntitySchemaReader.Read(Client(prefix: "x_"), typeof(Revision)).PhysicalName.Should().Be("x_revisions");
    }

    [Fact]
    public void Translation_sidecar_carries_its_fk_locale_unique_index()
    {
        var policy = new TranslationSidecarIndexPolicy(new Dictionary<Type, TranslationSidecarKey>
        {
            [typeof(FileTranslation)] = TranslationSidecarIndexPolicy.KeyFor(typeof(FileTranslation), "FileId", "Locale")
        });

        var table = EntitySchemaReader.Read(Client(policy, "sc_"), typeof(FileTranslation));

        table.PhysicalName.Should().Be("sc_file_translations");
        table.LogicalName.Should().Be("file_translations");
        table.TranslationLogicalTable.Should().Be("file_translations");
        table.Indexes.Should().ContainSingle().Which.Should().BeEquivalentTo(
            new ExpectedIndex("ux_file_translations_fk_locale", ["fileid", "locale"], true),
            o => o.WithStrictOrdering());
    }

    [Fact]
    public void Without_the_policy_a_sidecar_has_no_translation_index()
    {
        var table = EntitySchemaReader.Read(Client(prefix: "np_"), typeof(FileTranslation));

        table.TranslationLogicalTable.Should().BeNull();
        table.Indexes.Should().BeEmpty();
    }

    [Fact]
    public void Unique_group_names_are_qualified_with_their_table()
    {
        var client = Client(prefix: "ug_");

        EntitySchemaReader.Read(client, typeof(GroupAProbe)).Indexes.Should().ContainSingle().Which.Should()
            .BeEquivalentTo(new ExpectedIndex("ux_group_a_probe_unique1", ["code"], true));
        EntitySchemaReader.Read(client, typeof(GroupBProbe)).Indexes.Select(i => i.Name).Should()
            .Contain("ux_group_b_probe_unique1");
    }

    [Fact]
    public void A_unique_group_that_already_names_the_table_is_kept()
    {
        var indexes = EntitySchemaReader.Read(Client(prefix: "ug_"), typeof(GroupBProbe)).Indexes;

        indexes.Should().ContainSingle(i => i.Name == "ux_group_b_probe_slug").Which.Columns.Should().Equal("slug");
    }

    [Fact]
    public void ReadAll_orders_by_physical_name_and_covers_every_framework_type()
    {
        var tables = EntitySchemaReader.ReadAll(Client(), FrameworkEntityTypes.All);

        tables.Should().HaveCount(FrameworkEntityTypes.All.Count);
        tables.Select(t => t.PhysicalName).Should().BeInAscendingOrder(StringComparer.Ordinal);
        tables.Should().OnlyContain(t => t.IsFrameworkTable && t.PhysicalName.StartsWith("struo_"));
    }
}
