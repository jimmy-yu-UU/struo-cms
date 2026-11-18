using AwesomeAssertions;
using SqlSugar;
using Struo.Domain.Metadata;
using Struo.Domain.Metadata.Attributes;
using Struo.Domain.Metadata.Enums;
using Struo.Infrastructure.Metadata;
using Struo.Infrastructure.Persistence;
using Struo.Tests.Support;
using Xunit;

namespace Struo.Tests.Metadata;

// Each rule the interface x property-type check adds: the scan fails, and the message names the
// field, the problem and what to change.
public sealed class InterfacePropertyTypeMessageTests
{
    public enum Colour { Red, Green }

    private static string ScanOne(FieldInterface face, Type type, bool options = false)
    {
        var assembly = EmittedEntities.Build(
            EmittedEntities.UniqueName("msg"),
            [new EmittedEntity("Probe", [new EmittedProperty("V", type, face, options)])]);
        var act = () => MetadataScanner.ScanTypes([assembly.GetType("Emitted.Probe")!]);
        return act.Should().Throw<MetadataException>().Which.Message;
    }

    private const string Why = ": a field value is read and written as the property type its interface expects.";

    public static TheoryData<FieldInterface, Type, string> Messages() => new()
    {
        { FieldInterface.Text, typeof(DateOnly), "Field 'Probe.V' is a DateOnly property with Interface = Text, and no field interface accepts DateOnly; declare the property as DateTime (Interface = Date, Time or DateTime)" + Why },
        { FieldInterface.Date, typeof(DateTimeOffset?), "Field 'Probe.V' is a DateTimeOffset? property with Interface = Date, and no field interface accepts DateTimeOffset?; declare the property as DateTime (Interface = Date, Time or DateTime)" + Why },
        { FieldInterface.Time, typeof(TimeOnly?), "Field 'Probe.V' is a TimeOnly? property with Interface = Time, and no field interface accepts TimeOnly?; declare the property as DateTime (Interface = Time), or as string (Interface = Text) to hold the text" + Why },
        { FieldInterface.Number, typeof(TimeSpan), "Field 'Probe.V' is a TimeSpan property with Interface = Number, and no field interface accepts TimeSpan; declare the property as DateTime (Interface = Time), or as string (Interface = Text) to hold the text" + Why },
        { FieldInterface.Text, typeof(char), "Field 'Probe.V' is a char property with Interface = Text, and no field interface accepts char; declare the property as string" + Why },
        { FieldInterface.Select, typeof(Colour), "Field 'Probe.V' is a Colour property with Interface = Select, and no field interface accepts Colour; declare the property as string with Interface = Select or Radio and the values in [CmsOptions]" + Why },
        { FieldInterface.Select, typeof(Colour?), "Field 'Probe.V' is a Colour? property with Interface = Select, and no field interface accepts Colour?; declare the property as string with Interface = Select or Radio and the values in [CmsOptions]" + Why },
        { FieldInterface.Number, typeof(short), "Field 'Probe.V' is a short property with Interface = Number, and no field interface accepts short; declare the property as int or long (Interface = Number)" + Why },
        { FieldInterface.Number, typeof(uint), "Field 'Probe.V' is a uint property with Interface = Number, and no field interface accepts uint; declare the property as int or long (Interface = Number)" + Why },
        { FieldInterface.Number, typeof(ushort?), "Field 'Probe.V' is a ushort? property with Interface = Number, and no field interface accepts ushort?; declare the property as int or long (Interface = Number)" + Why },
        { FieldInterface.Number, typeof(sbyte), "Field 'Probe.V' is a sbyte property with Interface = Number, and no field interface accepts sbyte; declare the property as int or long (Interface = Number)" + Why },
        { FieldInterface.Text, typeof(int?), "Field 'Probe.V' is an int? property with Interface = Text, but Text holds string, Guid or Guid?; declare the property as string, Guid or Guid?, or change the interface to Number, Slider or Rating" + Why },
        { FieldInterface.Tags, typeof(bool), "Field 'Probe.V' is a bool property with Interface = Tags, but Tags holds List<TagItem>; declare the property as List<TagItem>, or change the interface to Boolean or Checkbox" + Why },
        { FieldInterface.Tags, typeof(List<string>), "Field 'Probe.V' is a List<string> property with Interface = Tags, but Tags holds List<TagItem>; declare the property as List<TagItem>, or change the interface to MultiSelect or CheckboxGroup" + Why },
        { FieldInterface.MultiSelect, typeof(List<int>), "Field 'Probe.V' is a List<int> property with Interface = MultiSelect, but MultiSelect holds List<string>; declare the property as List<string>" + Why },
        { FieldInterface.Number, typeof(Guid), "Field 'Probe.V' is a Guid property with Interface = Number, but Number holds int, int?, long, long?, decimal, decimal?, double, double?, float or float?; declare the property as one of those types, or change the interface to Text, File, Image or Uuid" + Why },
        { FieldInterface.Uuid, typeof(string), "Field 'Probe.V' is a string property with Interface = Uuid, but Uuid holds Guid or Guid?; declare the property as Guid or Guid?, or change the interface to Text, Textarea, RichText, Markdown or another interface that holds string (the field types chapter of the manual lists them)" + Why },
        { FieldInterface.Divider, typeof(byte[]), "Field 'Probe.V' is a byte[] property with Interface = Divider, but Divider holds the single-value types the other interfaces hold, except byte[]; declare the property as one of those types" + Why },
        { FieldInterface.Date, typeof(int), "Field 'Probe.V' is an int property with Interface = Date, but Date holds DateTime or DateTime?; declare the property as DateTime or DateTime?, or change the interface to Number, Slider or Rating" + Why },
    };

    [Theory]
    [MemberData(nameof(Messages))]
    public void A_property_type_the_interface_cannot_hold_is_reported_with_what_to_change(
        FieldInterface face, Type type, string expected)
    {
        ScanOne(face, type).Should().Be(expected);
    }

    [Fact]
    public void No_message_names_an_internal_class_and_none_ends_a_type_with_a_full_stop()
    {
        foreach (var (face, type) in new (FieldInterface, Type)[]
                 { (FieldInterface.Text, typeof(int?)), (FieldInterface.Time, typeof(TimeOnly?)), (FieldInterface.Number, typeof(Guid)) })
        {
            var message = ScanOne(face, type);
            message.Should().NotContain("Struo.").And.NotContain("InterfacePropertyTypes").And.NotContain("Nullable<")
                .And.NotContain("?.");
        }
    }

    [Theory]
    [InlineData(FieldInterface.MultiSelect)]
    [InlineData(FieldInterface.CheckboxGroup)]
    public void A_choice_of_several_without_options_is_rejected(FieldInterface face)
    {
        var message = ScanOne(face, typeof(List<string>), options: false);

        message.Should().Contain("Field 'Probe.V'").And.Contain($"Interface = {face}")
            .And.Contain("no [CmsOptions]").And.Contain("Add [CmsOptions(");
    }

    // ---- column declarations ----

    [CmsCollection("Json on string", Name = "iptJsonOnString")]
    public sealed class JsonOnString
    {
        [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }
        [SugarColumn(IsJson = true)]
        [CmsField(Label = "Body", Interface = FieldInterface.Hidden)] public string Body { get; set; } = "";
    }

    [CmsCollection("Json on json interface", Name = "iptJsonOnJsonInterface")]
    public sealed class JsonOnJsonInterface
    {
        [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }
        [SugarColumn(IsJson = true)]
        [CmsField(Label = "Body", Interface = FieldInterface.Json)] public string Body { get; set; } = "";
    }

    [CmsCollection("Json on int", Name = "iptJsonOnInt")]
    public sealed class JsonOnInt
    {
        [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }
        [SugarColumn(IsJson = true)]
        [CmsField(Label = "Count", Interface = FieldInterface.Number)] public int Count { get; set; }
    }

    [CmsCollection("Long text on int", Name = "iptLongTextOnInt")]
    public sealed class LongTextOnInt
    {
        [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }
        [ColumnShape(ColumnShape.LongText)]
        [CmsField(Label = "Count", Interface = FieldInterface.Number)] public int Count { get; set; }
    }

    [CmsCollection("Timestamp on int", Name = "iptTimestampOnInt")]
    public sealed class TimestampOnInt
    {
        [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }
        [ColumnShape(ColumnShape.Timestamp)]
        [CmsField(Label = "Count", Interface = FieldInterface.Number)] public int Count { get; set; }
    }

    [CmsCollection("Zoned on string", Name = "iptZonedOnString")]
    public sealed class ZonedOnString
    {
        [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }
        [ColumnShape(ColumnShape.TimestampWithTimeZone)]
        [CmsField(Label = "Note", Interface = FieldInterface.Text)] public string Note { get; set; } = "";
    }

    [CmsCollection("Zoned on date", Name = "iptZonedOnDate")]
    public sealed class ZonedOnDate
    {
        [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }
        [ColumnShape(ColumnShape.TimestampWithTimeZone)]
        [CmsField(Label = "At", Interface = FieldInterface.DateTime)] public DateTime? At { get; set; }
        [ColumnShape(ColumnShape.Timestamp)]
        [CmsField(Label = "Seen", Interface = FieldInterface.DateTime)] public DateTime Seen { get; set; }
        [ColumnShape(ColumnShape.LongText)]
        [CmsField(Label = "Body", Interface = FieldInterface.Textarea)] public string? Body { get; set; }
        [SugarColumn(IsJson = true)]
        [CmsField(Label = "Items", Interface = FieldInterface.Hidden)] public List<string> Items { get; set; } = [];
    }

    public static TheoryData<Type, string, string, string> ColumnRules() => new()
    {
        { typeof(JsonOnString), "JsonOnString.Body", "[SugarColumn(IsJson = true)]", "Remove IsJson" },
        { typeof(JsonOnJsonInterface), "JsonOnJsonInterface.Body", "[SugarColumn(IsJson = true)]", "Remove IsJson" },
        { typeof(JsonOnInt), "JsonOnInt.Count", "[SugarColumn(IsJson = true)]", "Remove IsJson" },
        { typeof(LongTextOnInt), "LongTextOnInt.Count", "[ColumnShape(LongText)]", "declare the property as string" },
        { typeof(TimestampOnInt), "TimestampOnInt.Count", "[ColumnShape(Timestamp)]", "declare the property as DateTime" },
        { typeof(ZonedOnString), "ZonedOnString.Note", "[ColumnShape(TimestampWithTimeZone)]", "declare the property as DateTime" },
    };

    [Theory]
    [MemberData(nameof(ColumnRules))]
    public void A_column_declaration_the_property_type_cannot_use_is_rejected(
        Type type, string property, string declaration, string fix)
    {
        var act = () => MetadataScanner.ScanTypes([type]);

        act.Should().Throw<MetadataException>().Which.Message
            .Should().Contain($"'{property}'").And.Contain(declaration).And.Contain(fix);
    }

    [Fact]
    public void The_matching_declarations_scan()
    {
        var fields = MetadataScanner.ScanTypes([typeof(ZonedOnDate)]).Single().Fields.Select(f => f.Name);

        fields.Should().Contain(["at", "seen", "body", "items"]);
    }

    // ---- every declaration site ----

    [CmsCollection("Ignored", Name = "iptIgnored")]
    public sealed class Ignored
    {
        [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }
        [SugarColumn(IsIgnore = true)]
        [CmsField(Label = "Note", Interface = FieldInterface.Divider)] public DateOnly Note { get; set; }
        [SugarColumn(IsIgnore = true)]
        [CmsField(Label = "Pick", Interface = FieldInterface.MultiSelect)] public List<string> Pick { get; set; } = [];
        [CmsField(Label = "Name", Interface = FieldInterface.Text)] public string Name { get; set; } = "";
    }

    [Fact]
    public void An_ignored_field_is_not_checked()
    {
        var fields = MetadataScanner.ScanTypes([typeof(Ignored)]).Single().Fields.Select(f => f.Name);

        fields.Should().Contain(["note", "pick", "name"]);
    }

    [CmsCollection("Sidecar owner", Name = "iptSidecarOwner")]
    public sealed class SidecarOwner
    {
        [SugarColumn(IsPrimaryKey = true)] public long Id { get; set; }
        [CmsTranslations(typeof(SidecarOwnerTranslation))]
        [SugarColumn(IsIgnore = true)] public List<SidecarOwnerTranslation> Translations { get; set; } = [];
    }

    public sealed class SidecarOwnerTranslation
    {
        [SugarColumn(IsPrimaryKey = true)] public long Id { get; set; }
        public long SidecarOwnerId { get; set; }
        public string Locale { get; set; } = "";
        [CmsField(Label = "Title", Interface = FieldInterface.Text)] public DateOnly Title { get; set; }
    }

    [CmsCollection("Link payload", Name = "iptLinkPayload", Hidden = true)]
    public sealed class LinkPayload
    {
        [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }
        [CmsField(Label = "Left", Interface = FieldInterface.Uuid)] public Guid LeftId { get; set; }
        [CmsField(Label = "Right", Interface = FieldInterface.Uuid)] public Guid RightId { get; set; }
        [CmsField(Label = "Note", Interface = FieldInterface.Tags)] public bool Note { get; set; }
    }

    public sealed class RepeaterRow
    {
        [CmsField(Label = "Name", Interface = FieldInterface.Text)] public List<string> Name { get; set; } = [];
    }

    public sealed class RepeaterRowWithDate
    {
        [CmsField(Label = "When", Interface = FieldInterface.Date)] public DateOnly When { get; set; }
    }

    public sealed class RepeaterRowWithObject
    {
        [CmsField(Label = "Any", Interface = FieldInterface.Text)] public object Any { get; set; } = "";
    }

    public sealed class RepeaterRowWithMismatch
    {
        [CmsField(Label = "Count", Interface = FieldInterface.Number)] public string Count { get; set; } = "";
    }

    [CmsCollection("Repeater owner", Name = "iptRepeaterOwner")]
    public sealed class RepeaterOwner
    {
        [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }
        [CmsField(Label = "Rows", Interface = FieldInterface.Repeater)] public List<RepeaterRow> Rows { get; set; } = [];
    }

    [CmsCollection("Repeater date owner", Name = "iptRepeaterDateOwner")]
    public sealed class RepeaterDateOwner
    {
        [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }
        [CmsField(Label = "Rows", Interface = FieldInterface.Repeater)] public List<RepeaterRowWithDate> Rows { get; set; } = [];
    }

    [CmsCollection("Repeater object owner", Name = "iptRepeaterObjectOwner")]
    public sealed class RepeaterObjectOwner
    {
        [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }
        [CmsField(Label = "Rows", Interface = FieldInterface.Repeater)] public List<RepeaterRowWithObject> Rows { get; set; } = [];
    }

    [CmsCollection("Repeater mismatch owner", Name = "iptRepeaterMismatchOwner")]
    public sealed class RepeaterMismatchOwner
    {
        [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }
        [CmsField(Label = "Rows", Interface = FieldInterface.Repeater)] public List<RepeaterRowWithMismatch> Rows { get; set; } = [];
    }

    public static TheoryData<Type, string, string> Sites() => new()
    {
        { typeof(SidecarOwner), "Field 'SidecarOwnerTranslation.Title'", "no field interface accepts DateOnly" },
        { typeof(LinkPayload), "Field 'LinkPayload.Note'", "Tags holds List<TagItem>" },
        { typeof(RepeaterOwner), "Sub-field 'RepeaterRow.Name' of Repeater 'RepeaterOwner.Rows'", "which holds a single value" },
        { typeof(RepeaterDateOwner), "Sub-field 'RepeaterRowWithDate.When' of Repeater 'RepeaterDateOwner.Rows'", "no field interface accepts DateOnly" },
        { typeof(RepeaterObjectOwner), "Field 'RepeaterObjectOwner.Rows'", "contains an object (member RepeaterRowWithObject.Any)" },
        { typeof(RepeaterMismatchOwner), "Sub-field 'RepeaterRowWithMismatch.Count' of Repeater 'RepeaterMismatchOwner.Rows'", "Number holds int" },
    };

    [Theory]
    [MemberData(nameof(Sites))]
    public void A_translation_sidecar_a_junction_payload_and_a_Repeater_sub_field_are_checked(
        Type type, string subject, string problem)
    {
        var act = () => MetadataScanner.ScanTypes([type]);

        act.Should().Throw<MetadataException>().Which.Message.Should().Contain(subject).And.Contain(problem);
    }

    // ---- [CmsOptions] with no entry ----

    [CmsCollection("Empty options", Name = "iptEmptyOptions")]
    public sealed class EmptyOptions
    {
        [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }
        [CmsOptions]
        [CmsField(Label = "Pick", Interface = FieldInterface.MultiSelect)] public List<string> Pick { get; set; } = [];
    }

    [Fact]
    public void A_choice_of_several_with_an_empty_options_attribute_is_rejected()
    {
        var act = () => MetadataScanner.ScanTypes([typeof(EmptyOptions)]);

        act.Should().Throw<MetadataException>().Which.Message.Should().Be(
            "Field 'EmptyOptions.Pick' has Interface = MultiSelect but a [CmsOptions] that lists no value, so every value " +
            "would be rejected as not one of its options. Add [CmsOptions(\"value:Label\", ...)] listing the allowed values.");
    }

    [Fact]
    public void The_message_for_a_choice_without_the_attribute_says_so()
    {
        ScanOne(FieldInterface.CheckboxGroup, typeof(List<string>)).Should().Be(
            "Field 'Probe.V' has Interface = CheckboxGroup but no [CmsOptions], so every value would be rejected as not " +
            "one of its options. Add [CmsOptions(\"value:Label\", ...)] listing the allowed values.");
    }

    [Fact]
    public void The_article_before_a_type_follows_the_word_that_is_printed()
    {
        var act = () => MetadataScanner.ScanTypes([typeof(LongTextOnInt)]);

        act.Should().Throw<MetadataException>().Which.Message.Should().Be(
            "Field 'LongTextOnInt.Count' has [ColumnShape(LongText)] and is an int property. Remove [ColumnShape], or " +
            "declare the property as string: the shape gives the column a text type that an int cannot be read from.");
    }

    // ---- an ignored Repeater ----

    [CmsCollection("Ignored repeater", Name = "iptIgnoredRepeater")]
    public sealed class IgnoredRepeater
    {
        [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }
        [CmsField(Label = "Name", Interface = FieldInterface.Text)] public string Name { get; set; } = "";
        [SugarColumn(IsIgnore = true)]
        [CmsField(Label = "Rows", Interface = FieldInterface.Repeater)] public List<RepeaterRowWithDate> Rows { get; set; } = [];
    }

    [Fact]
    public void The_sub_fields_of_an_ignored_Repeater_are_not_checked()
    {
        var fields = MetadataScanner.ScanTypes([typeof(IgnoredRepeater)]).Single().Fields.Select(f => f.Name);

        fields.Should().Contain(["name", "rows"]);
    }
}
