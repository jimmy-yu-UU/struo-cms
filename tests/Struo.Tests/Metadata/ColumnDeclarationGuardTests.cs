using AwesomeAssertions;
using SqlSugar;
using Struo.Domain.Metadata;
using Struo.Domain.Metadata.Attributes;
using Struo.Domain.Metadata.Enums;
using Struo.Infrastructure.Metadata;
using Struo.Infrastructure.Persistence;
using Xunit;

namespace Struo.Tests.Metadata;

// A [CmsField] property whose column declarations contradict each other fails the scan: the
// framework would otherwise ignore one of them or fail on first use. The scan covers collections,
// translation entities and junction collections; a Repeater sub-field has no column.
public sealed class ColumnDeclarationGuardTests
{
    // ---- collections ----

    [CmsCollection("Shape on JSON", Name = "cdgShapeOnJson")]
    public sealed class ShapeOnJson
    {
        [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }
        [ColumnShape(ColumnShape.LongText)]
        [CmsField(Label = "Tags", Interface = FieldInterface.Tags)] public List<string> Tags { get; set; } = [];
    }

    [CmsCollection("Type on JSON", Name = "cdgTypeOnJson")]
    public sealed class TypeOnJson
    {
        [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }
        [SugarColumn(ColumnDataType = "jsonb")]
        [CmsField(Label = "Tags", Interface = FieldInterface.Tags)] public List<string> Tags { get; set; } = [];
    }

    [CmsCollection("Shape and type", Name = "cdgShapeAndType")]
    public sealed class ShapeAndType
    {
        [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }
        [ColumnShape(ColumnShape.LongText)]
        [SugarColumn(ColumnDataType = "varchar(7)")]
        [CmsField(Label = "Body", Interface = FieldInterface.Text)] public string? Body { get; set; }
    }

    [CmsCollection("IsJson and type", Name = "cdgIsJsonAndType")]
    public sealed class IsJsonAndType
    {
        [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }
        [SugarColumn(IsJson = true, ColumnDataType = "jsonb")]
        [CmsField(Label = "Items", Interface = FieldInterface.Text)] public List<string> Items { get; set; } = [];
    }

    [CmsCollection("List and type", Name = "cdgListAndType")]
    public sealed class ListAndType
    {
        [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }
        [SugarColumn(ColumnDataType = "jsonb")]
        [CmsField(Label = "Items", Interface = FieldInterface.Text)] public List<string> Items { get; set; } = [];
    }

    // ---- translation entities ----

    [CmsCollection("Tr shape on JSON", Name = "cdgTrShapeOnJson")]
    public sealed class TrShapeOnJson
    {
        [SugarColumn(IsPrimaryKey = true)] public long Id { get; set; }
        [CmsTranslations(typeof(TrShapeOnJsonTranslation))]
        [SugarColumn(IsIgnore = true)] public List<TrShapeOnJsonTranslation> Translations { get; set; } = [];
    }

    public sealed class TrShapeOnJsonTranslation
    {
        [SugarColumn(IsPrimaryKey = true)] public long Id { get; set; }
        public long TrShapeOnJsonId { get; set; }
        public string Locale { get; set; } = "";
        [ColumnShape(ColumnShape.LongText)]
        [CmsField(Label = "Tags", Interface = FieldInterface.Tags)] public List<string> Tags { get; set; } = [];
    }

    [CmsCollection("Tr type on JSON", Name = "cdgTrTypeOnJson")]
    public sealed class TrTypeOnJson
    {
        [SugarColumn(IsPrimaryKey = true)] public long Id { get; set; }
        [CmsTranslations(typeof(TrTypeOnJsonTranslation))]
        [SugarColumn(IsIgnore = true)] public List<TrTypeOnJsonTranslation> Translations { get; set; } = [];
    }

    public sealed class TrTypeOnJsonTranslation
    {
        [SugarColumn(IsPrimaryKey = true)] public long Id { get; set; }
        public long TrTypeOnJsonId { get; set; }
        public string Locale { get; set; } = "";
        [SugarColumn(ColumnDataType = "jsonb")]
        [CmsField(Label = "Tags", Interface = FieldInterface.Tags)] public List<string> Tags { get; set; } = [];
    }

    [CmsCollection("Tr shape and type", Name = "cdgTrShapeAndType")]
    public sealed class TrShapeAndType
    {
        [SugarColumn(IsPrimaryKey = true)] public long Id { get; set; }
        [CmsTranslations(typeof(TrShapeAndTypeTranslation))]
        [SugarColumn(IsIgnore = true)] public List<TrShapeAndTypeTranslation> Translations { get; set; } = [];
    }

    public sealed class TrShapeAndTypeTranslation
    {
        [SugarColumn(IsPrimaryKey = true)] public long Id { get; set; }
        public long TrShapeAndTypeId { get; set; }
        public string Locale { get; set; } = "";
        [ColumnShape(ColumnShape.LongText)]
        [SugarColumn(ColumnDataType = "varchar(7)")]
        [CmsField(Label = "Body", Interface = FieldInterface.Text)] public string Body { get; set; } = "";
    }

    [CmsCollection("Tr IsJson and type", Name = "cdgTrIsJsonAndType")]
    public sealed class TrIsJsonAndType
    {
        [SugarColumn(IsPrimaryKey = true)] public long Id { get; set; }
        [CmsTranslations(typeof(TrIsJsonAndTypeTranslation))]
        [SugarColumn(IsIgnore = true)] public List<TrIsJsonAndTypeTranslation> Translations { get; set; } = [];
    }

    public sealed class TrIsJsonAndTypeTranslation
    {
        [SugarColumn(IsPrimaryKey = true)] public long Id { get; set; }
        public long TrIsJsonAndTypeId { get; set; }
        public string Locale { get; set; } = "";
        [SugarColumn(IsJson = true, ColumnDataType = "jsonb")]
        [CmsField(Label = "Items", Interface = FieldInterface.Text)] public List<string> Items { get; set; } = [];
    }

    // ---- junction collections (payload fields) ----

    [CmsCollection("Link shape on JSON", Name = "cdgLinkShapeOnJson", Hidden = true)]
    public sealed class LinkShapeOnJson
    {
        [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }
        [CmsField(Label = "Left", Interface = FieldInterface.Uuid)] public Guid LeftId { get; set; }
        [CmsField(Label = "Right", Interface = FieldInterface.Uuid)] public Guid RightId { get; set; }
        [ColumnShape(ColumnShape.LongText)]
        [CmsField(Label = "Tags", Interface = FieldInterface.Tags)] public List<string> Tags { get; set; } = [];
    }

    [CmsCollection("Link type on JSON", Name = "cdgLinkTypeOnJson", Hidden = true)]
    public sealed class LinkTypeOnJson
    {
        [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }
        [CmsField(Label = "Left", Interface = FieldInterface.Uuid)] public Guid LeftId { get; set; }
        [CmsField(Label = "Right", Interface = FieldInterface.Uuid)] public Guid RightId { get; set; }
        [SugarColumn(ColumnDataType = "jsonb")]
        [CmsField(Label = "Tags", Interface = FieldInterface.Tags)] public List<string> Tags { get; set; } = [];
    }

    [CmsCollection("Link shape and type", Name = "cdgLinkShapeAndType", Hidden = true)]
    public sealed class LinkShapeAndType
    {
        [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }
        [CmsField(Label = "Left", Interface = FieldInterface.Uuid)] public Guid LeftId { get; set; }
        [CmsField(Label = "Right", Interface = FieldInterface.Uuid)] public Guid RightId { get; set; }
        [ColumnShape(ColumnShape.LongText)]
        [SugarColumn(ColumnDataType = "varchar(7)")]
        [CmsField(Label = "Note", Interface = FieldInterface.Text)] public string? Note { get; set; }
    }

    [CmsCollection("Link IsJson and type", Name = "cdgLinkIsJsonAndType", Hidden = true)]
    public sealed class LinkIsJsonAndType
    {
        [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }
        [CmsField(Label = "Left", Interface = FieldInterface.Uuid)] public Guid LeftId { get; set; }
        [CmsField(Label = "Right", Interface = FieldInterface.Uuid)] public Guid RightId { get; set; }
        [SugarColumn(IsJson = true, ColumnDataType = "jsonb")]
        [CmsField(Label = "Items", Interface = FieldInterface.Text)] public List<string> Items { get; set; } = [];
    }

    public static TheoryData<Type, string, string> Rejected() => new()
    {
        // G1: [ColumnShape] on a JSON interface
        { typeof(ShapeOnJson), "ShapeOnJson.Tags", "[ColumnShape] and Interface = Tags" },
        { typeof(TrShapeOnJson), "TrShapeOnJsonTranslation.Tags", "[ColumnShape] and Interface = Tags" },
        { typeof(LinkShapeOnJson), "LinkShapeOnJson.Tags", "[ColumnShape] and Interface = Tags" },
        // G3: ColumnDataType on a JSON interface
        { typeof(TypeOnJson), "TypeOnJson.Tags", "ColumnDataType = \"jsonb\")] and Interface = Tags" },
        { typeof(TrTypeOnJson), "TrTypeOnJsonTranslation.Tags", "ColumnDataType = \"jsonb\")] and Interface = Tags" },
        { typeof(LinkTypeOnJson), "LinkTypeOnJson.Tags", "ColumnDataType = \"jsonb\")] and Interface = Tags" },
        // G2: [ColumnShape] with ColumnDataType
        { typeof(ShapeAndType), "ShapeAndType.Body", "both [ColumnShape(LongText)] and [SugarColumn(ColumnDataType = \"varchar(7)\")]" },
        { typeof(TrShapeAndType), "TrShapeAndTypeTranslation.Body", "both [ColumnShape(LongText)] and [SugarColumn(ColumnDataType = \"varchar(7)\")]" },
        { typeof(LinkShapeAndType), "LinkShapeAndType.Note", "both [ColumnShape(LongText)] and [SugarColumn(ColumnDataType = \"varchar(7)\")]" },
        // G5: IsJson with ColumnDataType, and a List<> (stored as JSON) with ColumnDataType
        { typeof(IsJsonAndType), "IsJsonAndType.Items", "ColumnDataType = \"jsonb\")] and [SugarColumn(IsJson = true)]" },
        { typeof(TrIsJsonAndType), "TrIsJsonAndTypeTranslation.Items", "ColumnDataType = \"jsonb\")] and [SugarColumn(IsJson = true)]" },
        { typeof(LinkIsJsonAndType), "LinkIsJsonAndType.Items", "ColumnDataType = \"jsonb\")] and [SugarColumn(IsJson = true)]" },
        { typeof(ListAndType), "ListAndType.Items", "ColumnDataType = \"jsonb\")] and a List<> type" },
    };

    [Theory]
    [MemberData(nameof(Rejected))]
    public void A_contradicting_declaration_fails_the_scan_naming_the_property_and_both_declarations(
        Type type, string property, string declarations)
    {
        var act = () => MetadataScanner.ScanTypes([type]);

        var message = act.Should().Throw<MetadataException>().Which.Message;
        message.Should().Contain($"'{property}'").And.Contain(declarations);
        message.Should().Contain("Remove ").And.NotContain("Struo.Infrastructure");
    }

    [Theory]
    [MemberData(nameof(Rejected))]
    public void Every_rejection_message_is_ascii_and_at_most_two_sentences(Type type, string property, string declarations)
    {
        var act = () => MetadataScanner.ScanTypes([type]);

        var message = act.Should().Throw<MetadataException>().Which.Message;
        message.All(c => c < 128).Should().BeTrue(message);
        message.Split(". ").Length.Should().BeLessThanOrEqualTo(2, message);
        property.Should().NotBeNullOrEmpty();
        declarations.Should().NotBeNullOrEmpty();
    }

    // ---- allowed neighbours ----

    public sealed class RepeaterChild
    {
        // No column exists for a sub-field, so none of the column rules applies.
        [ColumnShape(ColumnShape.LongText)]
        [SugarColumn(ColumnDataType = "varchar(7)")]
        [CmsField(Label = "Label", Interface = FieldInterface.Text)] public string Label { get; set; } = "";
    }

    [CmsCollection("Allowed", Name = "cdgAllowed")]
    public sealed class Allowed
    {
        [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }

        [ColumnShape(ColumnShape.LongText)]
        [CmsField(Label = "Body", Interface = FieldInterface.RichText)] public string Body { get; set; } = "";

        [SugarColumn(ColumnDataType = "varchar(500)")]
        [CmsField(Label = "Plain", Interface = FieldInterface.Text)] public string? Plain { get; set; }

        [SugarColumn(ColumnDataType = "text")]
        [CmsField(Label = "Long", Interface = FieldInterface.Textarea)] public string? Long { get; set; }

        [ColumnShape(ColumnShape.LongText)]
        [CmsField(Label = "Shaped", Interface = FieldInterface.Text)] public string? Shaped { get; set; }

        [SugarColumn(IsJson = true)]
        [CmsField(Label = "Marked", Interface = FieldInterface.Text)] public List<string> Marked { get; set; } = [];

        [CmsField(Label = "Unmarked", Interface = FieldInterface.Text)] public List<string> Unmarked { get; set; } = [];

        [CmsField(Label = "Tags", Interface = FieldInterface.Tags)] public List<string> Tags { get; set; } = [];

        [ColumnShape(ColumnShape.LongText)]
        [SugarColumn(IsIgnore = true, ColumnDataType = "varchar(7)")]
        [CmsField(Label = "Ignored", Interface = FieldInterface.Divider)] public string? Ignored { get; set; }

        [CmsField(Label = "Rows", Interface = FieldInterface.Repeater)] public List<RepeaterChild> Rows { get; set; } = [];
    }

    [Fact]
    public void Allowed_neighbours_of_the_rejected_combinations_scan()
    {
        var collection = MetadataScanner.ScanTypes([typeof(Allowed)]).Single();

        collection.Fields.Select(f => f.Name).Should().Contain(["body", "plain", "long", "shaped", "marked", "unmarked", "tags", "rows"]);
    }

    [Fact]
    public void A_Repeater_sub_field_is_not_checked_because_it_has_no_column()
    {
        var rows = MetadataScanner.ScanTypes([typeof(Allowed)]).Single().Fields.Single(f => f.Name == "rows");

        rows.Fields!.Single().Name.Should().Be("label");
    }
}
