using AwesomeAssertions;
using SqlSugar;
using Struo.Domain.Metadata;
using Struo.Domain.Metadata.Attributes;
using Struo.Domain.Metadata.Enums;
using Struo.Infrastructure.Metadata;
using Xunit;

namespace Struo.Tests.Metadata;

// A [CmsField] column property whose CLR type is not a single value sits on a multi-value interface
// or on Hidden; any other interface fails the scan, with or without IsJson. The Json interface holds
// raw JSON text, so its property is a string.
public sealed class StructuredFieldInterfaceTests
{
    public sealed class Custom
    {
        public string A { get; set; } = "";
        public int B { get; set; }
    }

    public enum Colour { Red, Green }

    // ---- rejected: one collection per combination, so the message can name the class ----

    [CmsCollection("Text list", Name = "sfiTextList")]
    public sealed class TextList
    {
        [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }
        [CmsField(Label = "Value", Interface = FieldInterface.Text)] public List<string> Value { get; set; } = [];
    }

    [CmsCollection("Text array", Name = "sfiTextArray")]
    public sealed class TextArray
    {
        [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }
        [CmsField(Label = "Value", Interface = FieldInterface.Text)] public string[] Value { get; set; } = [];
    }

    [CmsCollection("Text map", Name = "sfiTextMap")]
    public sealed class TextMap
    {
        [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }
        [CmsField(Label = "Value", Interface = FieldInterface.Text)] public Dictionary<string, string> Value { get; set; } = new();
    }

    [CmsCollection("Text custom", Name = "sfiTextCustom")]
    public sealed class TextCustom
    {
        [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }
        [CmsField(Label = "Value", Interface = FieldInterface.Text)] public Custom Value { get; set; } = new();
    }

    [CmsCollection("Text list json", Name = "sfiTextListJson")]
    public sealed class TextListJson
    {
        [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }
        [SugarColumn(IsJson = true)]
        [CmsField(Label = "Value", Interface = FieldInterface.Text)] public List<string> Value { get; set; } = [];
    }

    [CmsCollection("Text array json", Name = "sfiTextArrayJson")]
    public sealed class TextArrayJson
    {
        [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }
        [SugarColumn(IsJson = true)]
        [CmsField(Label = "Value", Interface = FieldInterface.Text)] public string[] Value { get; set; } = [];
    }

    [CmsCollection("Text map json", Name = "sfiTextMapJson")]
    public sealed class TextMapJson
    {
        [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }
        [SugarColumn(IsJson = true)]
        [CmsField(Label = "Value", Interface = FieldInterface.Text)] public Dictionary<string, string> Value { get; set; } = new();
    }

    [CmsCollection("Text custom json", Name = "sfiTextCustomJson")]
    public sealed class TextCustomJson
    {
        [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }
        [SugarColumn(IsJson = true)]
        [CmsField(Label = "Value", Interface = FieldInterface.Text)] public Custom Value { get; set; } = new();
    }

    [CmsCollection("Select list", Name = "sfiSelectList")]
    public sealed class SelectList
    {
        [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }
        [CmsField(Label = "Value", Interface = FieldInterface.Select)] public List<string> Value { get; set; } = [];
    }

    [CmsCollection("Json list", Name = "sfiJsonList")]
    public sealed class JsonList
    {
        [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }
        [CmsField(Label = "Value", Interface = FieldInterface.Json)] public List<string> Value { get; set; } = [];
    }

    [CmsCollection("Json number", Name = "sfiJsonNumber")]
    public sealed class JsonNumber
    {
        [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }
        [CmsField(Label = "Value", Interface = FieldInterface.Json)] public int Value { get; set; }
    }

    [CmsCollection("Translated text list", Name = "sfiTrTextList")]
    public sealed class TrTextList
    {
        [SugarColumn(IsPrimaryKey = true)] public long Id { get; set; }
        [CmsTranslations(typeof(TrTextListTranslation))]
        [SugarColumn(IsIgnore = true)] public List<TrTextListTranslation> Translations { get; set; } = [];
    }

    public sealed class TrTextListTranslation
    {
        [SugarColumn(IsPrimaryKey = true)] public long Id { get; set; }
        public long TrTextListId { get; set; }
        public string Locale { get; set; } = "";
        [CmsField(Label = "Value", Interface = FieldInterface.Text)] public List<string> Value { get; set; } = [];
    }

    [CmsCollection("Link text list", Name = "sfiLinkTextList", Hidden = true)]
    public sealed class LinkTextList
    {
        [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }
        [CmsField(Label = "Left", Interface = FieldInterface.Uuid)] public Guid LeftId { get; set; }
        [CmsField(Label = "Right", Interface = FieldInterface.Uuid)] public Guid RightId { get; set; }
        [CmsField(Label = "Value", Interface = FieldInterface.Text)] public List<string> Value { get; set; } = [];
    }

    private const string StructuredFix = "Change the interface to one of MultiSelect, CheckboxGroup, Tags, KeyValue, Repeater, Files or Hidden";

    public static TheoryData<Type, string, string, string, string> Rejected() => new()
    {
        { typeof(TextList), "TextList.Value", "List<string>", "Interface = Text", StructuredFix },
        { typeof(TextArray), "TextArray.Value", "string[]", "Interface = Text", StructuredFix },
        { typeof(TextMap), "TextMap.Value", "Dictionary<string, string>", "Interface = Text", StructuredFix },
        { typeof(TextCustom), "TextCustom.Value", "Custom", "Interface = Text", StructuredFix },
        { typeof(TextListJson), "TextListJson.Value", "List<string>", "Interface = Text", StructuredFix },
        { typeof(TextArrayJson), "TextArrayJson.Value", "string[]", "Interface = Text", StructuredFix },
        { typeof(TextMapJson), "TextMapJson.Value", "Dictionary<string, string>", "Interface = Text", StructuredFix },
        { typeof(TextCustomJson), "TextCustomJson.Value", "Custom", "Interface = Text", StructuredFix },
        { typeof(SelectList), "SelectList.Value", "List<string>", "Interface = Select", StructuredFix },
        { typeof(TrTextList), "TrTextListTranslation.Value", "List<string>", "Interface = Text", StructuredFix },
        { typeof(LinkTextList), "LinkTextList.Value", "List<string>", "Interface = Text", StructuredFix },
        { typeof(JsonList), "JsonList.Value", "List<string>", "Interface = Json", "Declare the property as string" },
        { typeof(JsonNumber), "JsonNumber.Value", "int", "Interface = Json", "Declare the property as string" },
    };

    private static string Article(string typeName) => "aeiou".Contains(char.ToLowerInvariant(typeName[0])) ? "an" : "a";

    [Theory]
    [MemberData(nameof(Rejected))]
    public void A_property_type_the_interface_cannot_hold_fails_the_scan_naming_the_property_both_declarations_and_the_fix(
        Type type, string property, string clrType, string declaredInterface, string fix)
    {
        var act = () => MetadataScanner.ScanTypes([type]);

        var message = act.Should().Throw<MetadataException>().Which.Message;
        message.Should().Contain($"'{property}'").And.Contain($"{Article(clrType)} {clrType} property")
            .And.Contain(declaredInterface).And.Contain(fix);
        message.All(c => c < 128).Should().BeTrue(message);
        message.Should().NotContain("Struo.Infrastructure");
    }

    [Fact]
    public void The_structured_message_offers_both_ways_out()
    {
        var act = () => MetadataScanner.ScanTypes([typeof(TextList)]);

        act.Should().Throw<MetadataException>().Which.Message
            .Should().Contain("or declare a single-value property");
    }

    [Fact]
    public void The_Json_message_says_the_field_holds_raw_JSON_text()
    {
        var act = () => MetadataScanner.ScanTypes([typeof(JsonList)]);

        act.Should().Throw<MetadataException>().Which.Message.Should().Contain("raw JSON text");
    }

    // ---- accepted: generic hosts, one per interface ----

    [CmsCollection("Text host", Name = "sfiTextHost")]
    public sealed class TextHost<T>
    {
        [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }
        [CmsField(Label = "Value", Interface = FieldInterface.Text)] public T Value { get; set; } = default!;
    }

    [CmsCollection("Hidden host", Name = "sfiHiddenHost")]
    public sealed class HiddenHost<T>
    {
        [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }
        [CmsField(Label = "Value", Interface = FieldInterface.Hidden)] public T Value { get; set; } = default!;
    }

    [CmsCollection("Json host", Name = "sfiJsonHost")]
    public sealed class JsonHost<T>
    {
        [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }
        [CmsField(Label = "Value", Interface = FieldInterface.Json)] public T Value { get; set; } = default!;
    }

    [CmsCollection("Multi select host", Name = "sfiMultiSelectHost")]
    public sealed class MultiSelectHost<T>
    {
        [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }
        [CmsOptions("a:A", "b:B")]
        [CmsField(Label = "Value", Interface = FieldInterface.MultiSelect)] public T Value { get; set; } = default!;
    }

    [CmsCollection("Checkbox group host", Name = "sfiCheckboxGroupHost")]
    public sealed class CheckboxGroupHost<T>
    {
        [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }
        [CmsOptions("a:A", "b:B")]
        [CmsField(Label = "Value", Interface = FieldInterface.CheckboxGroup)] public T Value { get; set; } = default!;
    }

    [CmsCollection("Tags host", Name = "sfiTagsHost")]
    public sealed class TagsHost<T>
    {
        [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }
        [CmsField(Label = "Value", Interface = FieldInterface.Tags)] public T Value { get; set; } = default!;
    }

    [CmsCollection("Key value host", Name = "sfiKeyValueHost")]
    public sealed class KeyValueHost<T>
    {
        [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }
        [CmsField(Label = "Value", Interface = FieldInterface.KeyValue)] public T Value { get; set; } = default!;
    }

    [CmsCollection("Files host", Name = "sfiFilesHost")]
    public sealed class FilesHost<T>
    {
        [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }
        [CmsField(Label = "Value", Interface = FieldInterface.Files)] public T Value { get; set; } = default!;
    }

    [CmsCollection("Repeater host", Name = "sfiRepeaterHost")]
    public sealed class RepeaterHost<T>
    {
        [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }
        [CmsField(Label = "Value", Interface = FieldInterface.Repeater)] public T Value { get; set; } = default!;
    }

    public sealed class RepeaterRow
    {
        [CmsField(Label = "Label", Interface = FieldInterface.Text)] public string Label { get; set; } = "";
    }

    public static TheoryData<Type> Accepted() => new()
    {
        typeof(HiddenHost<List<string>>), typeof(HiddenHost<string[]>),
        typeof(HiddenHost<Dictionary<string, string>>), typeof(HiddenHost<Custom>),
        typeof(JsonHost<string>), typeof(JsonHost<string?>),
        typeof(MultiSelectHost<List<string>>), typeof(CheckboxGroupHost<List<string>>),
        typeof(TagsHost<List<Struo.Domain.Metadata.Models.TagItem>>),
        typeof(KeyValueHost<Dictionary<string, string>>), typeof(FilesHost<List<Guid>>),
        typeof(RepeaterHost<List<RepeaterRow>>),
        typeof(TextHost<string>), typeof(TextHost<string?>), typeof(TextHost<Guid>), typeof(TextHost<Guid?>),
    };

    [Theory]
    [MemberData(nameof(Accepted))]
    public void A_property_type_the_interface_can_hold_scans(Type type)
    {
        var collection = MetadataScanner.ScanTypes([type]).Single();

        collection.Fields.Select(f => f.Name).Should().Contain("value");
    }

    // ---- rejected: object reads back as a JSON node, on every interface ----

    public static TheoryData<Type, string> ObjectRejected() => new()
    {
        { typeof(HiddenHost<object>), "Hidden" }, { typeof(TextHost<object>), "Text" },
        { typeof(MultiSelectHost<object>), "MultiSelect" },
        { typeof(CheckboxGroupHost<object>), "CheckboxGroup" }, { typeof(TagsHost<object>), "Tags" },
        { typeof(KeyValueHost<object>), "KeyValue" }, { typeof(FilesHost<object>), "Files" },
        { typeof(RepeaterHost<object>), "Repeater" },
    };

    [Theory]
    [MemberData(nameof(ObjectRejected))]
    public void An_object_property_fails_the_scan_on_every_interface_and_asks_for_the_concrete_type(
        Type type, string declaredInterface)
    {
        var act = () => MetadataScanner.ScanTypes([type]);

        var message = act.Should().Throw<MetadataException>().Which.Message;
        message.Should().Contain(".Value'").And.Contain("an object property")
            .And.Contain($"Interface = {declaredInterface}").And.Contain("Declare the concrete type");
        message.All(c => c < 128).Should().BeTrue(message);
    }

    // ---- rejected: object anywhere inside the property type ----

    public sealed class HolderWithObject
    {
        public string Name { get; set; } = "";
        public object? Payload { get; set; }
    }

    public sealed class OuterWithInner
    {
        public HolderWithObject Inner { get; set; } = new();
    }

    public readonly struct StructWithObject
    {
        public object? Payload { get; init; }
    }

    public sealed class RowWithObjectSubField
    {
        [CmsField(Label = "Label", Interface = FieldInterface.Text)] public string Label { get; set; } = "";
        [CmsField(Label = "Payload", Interface = FieldInterface.Text)] public object? Payload { get; set; }
    }

    public sealed class RowWithObjectMember
    {
        [CmsField(Label = "Label", Interface = FieldInterface.Text)] public string Label { get; set; } = "";
        public object? Extra { get; set; }
    }

    public sealed class ProbeWrapper<T>
    {
        public T? Value { get; set; }
        public object? Extra { get; set; }
    }

    public sealed class RowWithUntyped
    {
        public System.Collections.ArrayList Items { get; set; } = [];
    }

    public static TheoryData<Type, string> ObjectNestedRejected() => new()
    {
        { typeof(HiddenHost<Systems.Foo.FooHolder>), "member FooHolder.Payload" },
        { typeof(HiddenHost<MicrosoftPartner.Bar.BarHolder>), "member BarHolder.Payload" },
        { typeof(HiddenHost<GlobalNamespaceHolder>), "member GlobalNamespaceHolder.Payload" },
        { typeof(HiddenHost<ProbeWrapper<int>>), "member ProbeWrapper<int>.Extra" },
        { typeof(HiddenHost<System.Collections.ArrayList>), "untyped elements of ArrayList" },
        { typeof(HiddenHost<System.Collections.Hashtable>), "untyped elements of Hashtable" },
        { typeof(HiddenHost<System.Collections.IList>), "untyped elements of IList" },
        { typeof(HiddenHost<List<System.Collections.ArrayList>>), "untyped elements of ArrayList" },
        { typeof(HiddenHost<RowWithUntyped>), "untyped elements of ArrayList" },
        { typeof(RepeaterHost<List<RowWithUntyped>>), "untyped elements of ArrayList" },
        { typeof(HiddenHost<Dictionary<string, object>>), "type argument of Dictionary<string, object>" },
        { typeof(HiddenHost<List<object>>), "type argument of List<object>" },
        { typeof(HiddenHost<object[]>), "element type of object[]" },
        { typeof(HiddenHost<HolderWithObject>), "member HolderWithObject.Payload" },
        { typeof(HiddenHost<OuterWithInner>), "member HolderWithObject.Payload" },
        { typeof(HiddenHost<List<HolderWithObject>>), "member HolderWithObject.Payload" },
        { typeof(HiddenHost<StructWithObject>), "member StructWithObject.Payload" },
        { typeof(HiddenHost<(int, object)>), "type argument of ValueTuple<int, object>" },
        { typeof(MultiSelectHost<List<object>>), "type argument of List<object>" },
        { typeof(KeyValueHost<Dictionary<string, object>>), "type argument of Dictionary<string, object>" },
        { typeof(RepeaterHost<List<RowWithObjectSubField>>), "member RowWithObjectSubField.Payload" },
        { typeof(RepeaterHost<List<RowWithObjectMember>>), "member RowWithObjectMember.Extra" },
    };

    [Theory]
    [MemberData(nameof(ObjectNestedRejected))]
    public void An_object_inside_the_property_type_fails_the_scan_and_names_where_it_is(Type type, string where)
    {
        var act = () => MetadataScanner.ScanTypes([type]);

        var message = act.Should().Throw<MetadataException>().Which.Message;
        message.Should().Contain(".Value'").And.Contain("contains an object").And.Contain(where)
            .And.Contain("Declare the concrete type");
        message.All(c => c < 128).Should().BeTrue(message);
    }

    [Theory]
    [InlineData(typeof(JsonHost<object>))]
    [InlineData(typeof(JsonHost<Dictionary<string, object>>))]
    [InlineData(typeof(JsonHost<List<object>>))]
    public void On_the_Json_interface_an_object_property_still_gets_the_string_rule(Type type)
    {
        var act = () => MetadataScanner.ScanTypes([type]);

        act.Should().Throw<MetadataException>().Which.Message
            .Should().Contain("raw JSON text").And.Contain("Declare the property as string");
    }

    [Fact]
    public void An_object_inside_a_type_that_does_not_fit_a_single_value_interface_reports_the_interface_first()
    {
        foreach (var type in new[] { typeof(TextHost<Dictionary<string, object>>), typeof(TextHost<HolderWithObject>) })
        {
            var act = () => MetadataScanner.ScanTypes([type]);

            var message = act.Should().Throw<MetadataException>().Which.Message;
            message.Should().Contain("which holds a single value").And.NotContain("contains an object");
        }
    }

    [Fact]
    public void The_article_before_the_type_name_agrees_with_it()
    {
        var act = () => MetadataScanner.ScanTypes([typeof(HiddenHost<object[][]>)]);

        act.Should().Throw<MetadataException>().Which.Message
            .Should().Contain("is an object[][] property").And.NotContain("a object");
    }

    // A framework type is not walked: DictionaryEntry has object members that would be rejected if it were.
    [Theory]
    [InlineData(typeof(HiddenHost<System.Collections.DictionaryEntry>))]
    [InlineData(typeof(HiddenHost<string>))]
    [InlineData(typeof(HiddenHost<byte[]>))]
    public void A_framework_type_is_not_walked_and_string_and_byte_array_are_not_untyped_collections(Type type)
    {
        var act = () => MetadataScanner.ScanTypes([type]);

        act.Should().NotThrow();
    }

    public sealed class SelfReferencing
    {
        public string Name { get; set; } = "";
        public SelfReferencing? Next { get; set; }
        public List<SelfReferencing> Children { get; set; } = [];
    }

    public sealed class MutualA { public MutualB? B { get; set; } }
    public sealed class MutualB { public MutualA? A { get; set; } public int N { get; set; } }

    [Theory]
    [InlineData(typeof(HiddenHost<SelfReferencing>))]
    [InlineData(typeof(HiddenHost<MutualA>))]
    [InlineData(typeof(HiddenHost<Dictionary<string, List<int>>>))]
    [InlineData(typeof(HiddenHost<Dictionary<string, Custom>>))]
    [InlineData(typeof(HiddenHost<Guid[]>))]
    [InlineData(typeof(HiddenHost<(int, string)>))]
    [InlineData(typeof(HiddenHost<System.Text.Json.JsonElement>))]
    public void A_structured_type_without_object_scans_and_a_cycle_ends(Type type)
    {
        var act = () => MetadataScanner.ScanTypes([type]);

        act.Should().NotThrow();
    }

    [Fact]
    public void An_ignored_object_property_is_not_checked()
    {
        var act = () => MetadataScanner.ScanTypes([typeof(IgnoredObject)]);

        act.Should().NotThrow();
    }

    [CmsCollection("Ignored object", Name = "sfiIgnoredObject")]
    public sealed class IgnoredObject
    {
        [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }
        [SugarColumn(IsIgnore = true)]
        [CmsField(Label = "Value", Interface = FieldInterface.Hidden)] public object Value { get; set; } = new();
    }

    [Fact]
    public void An_ignored_structured_property_is_not_checked()
    {
        var act = () => MetadataScanner.ScanTypes([typeof(IgnoredList)]);

        act.Should().NotThrow();
    }

    [CmsCollection("Ignored list", Name = "sfiIgnoredList")]
    public sealed class IgnoredList
    {
        [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }
        [SugarColumn(IsIgnore = true)]
        [CmsField(Label = "Value", Interface = FieldInterface.Text)] public List<string> Value { get; set; } = [];
    }

    // ---- the shared predicate ----

    public static TheoryData<Type, bool> Structured() => new()
    {
        { typeof(string), false }, { typeof(int), false }, { typeof(int?), false }, { typeof(bool), false },
        { typeof(char), false }, { typeof(decimal), false }, { typeof(Guid?), false }, { typeof(DateTime), false },
        { typeof(DateTimeOffset), false }, { typeof(DateOnly), false }, { typeof(TimeOnly?), false },
        { typeof(TimeSpan), false }, { typeof(Colour), false }, { typeof(Colour?), false }, { typeof(byte[]), false },
        { typeof(List<string>), true }, { typeof(IList<string>), true }, { typeof(string[]), true },
        { typeof(int[]), true }, { typeof(Dictionary<string, string>), true }, { typeof(HashSet<string>), true },
        { typeof(Custom), true }, { typeof(System.Text.Json.JsonElement), true },
        { typeof((int, int)), true }, { typeof(object), true },
    };

    [Theory]
    [MemberData(nameof(Structured))]
    public void The_predicate_classifies_a_property_type(Type type, bool structured)
    {
        StructuredType.Is(type).Should().Be(structured);
    }
}
