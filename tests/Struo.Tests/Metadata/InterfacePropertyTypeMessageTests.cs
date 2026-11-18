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

    [Theory]
    [InlineData(typeof(DateOnly), "DateOnly", "DateTime")]
    [InlineData(typeof(DateOnly?), "DateOnly?", "DateTime")]
    [InlineData(typeof(DateTimeOffset), "DateTimeOffset", "DateTime")]
    [InlineData(typeof(TimeOnly), "TimeOnly", "string")]
    [InlineData(typeof(TimeSpan), "TimeSpan", "string")]
    [InlineData(typeof(char), "char", "string")]
    [InlineData(typeof(Colour), "Colour", "[CmsOptions]")]
    [InlineData(typeof(Colour?), "Colour?", "[CmsOptions]")]
    [InlineData(typeof(short), "short", "int or long")]
    public void A_type_no_interface_accepts_is_named_with_the_type_to_use_instead(Type type, string shown, string instead)
    {
        var message = ScanOne(FieldInterface.Text, type);

        message.Should().Contain("Field 'Probe.V'")
            .And.Contain($"no field interface accepts {shown}")
            .And.Contain("Declare the property as").And.Contain(instead);
    }

    [Theory]
    [InlineData(FieldInterface.Tags, typeof(bool), "Tags holds List<TagItem>", "Boolean, Checkbox, Hidden or Divider")]
    [InlineData(FieldInterface.Tags, typeof(List<string>), "Tags holds List<TagItem>", "MultiSelect, CheckboxGroup or Hidden")]
    [InlineData(FieldInterface.MultiSelect, typeof(string), "MultiSelect holds List<string>", "Text, Textarea")]
    [InlineData(FieldInterface.Files, typeof(List<string>), "Files holds List<Guid>", "MultiSelect")]
    [InlineData(FieldInterface.KeyValue, typeof(Dictionary<string, int>), "KeyValue holds Dictionary<string, string>", "Hidden")]
    [InlineData(FieldInterface.Number, typeof(string), "Number holds int, int?, long", "Text, Textarea")]
    [InlineData(FieldInterface.Date, typeof(int), "Date holds DateTime or DateTime?", "Number, Slider, Rating")]
    [InlineData(FieldInterface.Text, typeof(int), "Text holds string, Guid or Guid?", "Number")]
    [InlineData(FieldInterface.Uuid, typeof(string), "Uuid holds Guid or Guid?", "Text")]
    [InlineData(FieldInterface.Divider, typeof(byte[]), "Divider holds string", "Hidden")]
    public void A_type_that_does_not_fit_the_interface_lists_what_fits_each_way(
        FieldInterface face, Type type, string holds, string others)
    {
        var message = ScanOne(face, type, options: face is FieldInterface.MultiSelect);

        message.Should().Contain("Field 'Probe.V'").And.Contain(holds)
            .And.Contain("or change the interface to").And.Contain(others);
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
}
