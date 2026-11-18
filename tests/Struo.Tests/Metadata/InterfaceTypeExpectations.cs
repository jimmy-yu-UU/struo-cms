using Struo.Domain.Metadata.Attributes;
using Struo.Domain.Metadata.Enums;
using Struo.Domain.Metadata.Models;

namespace Struo.Tests.Metadata;

// The expectations of the interface x property-type tests, written out here and not read from the
// product's table: the 40 property types and, for each of the 33 interfaces, the type ids it accepts.
public static class InterfaceTypeExpectations
{
    public enum Colour { Red, Green }

    public sealed class Plain
    {
        public string Name { get; set; } = "";
    }

    public sealed class Custom
    {
        [CmsField(Label = "Name", Interface = FieldInterface.Text)] public string Name { get; set; } = "";
        [CmsField(Label = "Weight", Interface = FieldInterface.Number)] public int Weight { get; set; }
    }

    public static readonly (string Id, Type Type)[] Types =
    [
        ("string", typeof(string)), ("int", typeof(int)), ("long", typeof(long)), ("decimal", typeof(decimal)),
        ("double", typeof(double)), ("float", typeof(float)), ("bool", typeof(bool)), ("DateTime", typeof(DateTime)),
        ("DateTimeOffset", typeof(DateTimeOffset)), ("DateOnly", typeof(DateOnly)), ("TimeOnly", typeof(TimeOnly)),
        ("TimeSpan", typeof(TimeSpan)), ("Guid", typeof(Guid)), ("enum", typeof(Colour)), ("byte[]", typeof(byte[])),
        ("char", typeof(char)),
        ("int?", typeof(int?)), ("long?", typeof(long?)), ("decimal?", typeof(decimal?)), ("double?", typeof(double?)),
        ("float?", typeof(float?)), ("bool?", typeof(bool?)), ("DateTime?", typeof(DateTime?)),
        ("DateTimeOffset?", typeof(DateTimeOffset?)), ("DateOnly?", typeof(DateOnly?)), ("TimeOnly?", typeof(TimeOnly?)),
        ("TimeSpan?", typeof(TimeSpan?)), ("Guid?", typeof(Guid?)), ("enum?", typeof(Colour?)), ("char?", typeof(char?)),
        ("List<string>", typeof(List<string>)), ("List<int>", typeof(List<int>)), ("List<Guid>", typeof(List<Guid>)),
        ("List<TagItem>", typeof(List<TagItem>)), ("string[]", typeof(string[])),
        ("Dictionary<string,string>", typeof(Dictionary<string, string>)),
        ("Dictionary<string,int>", typeof(Dictionary<string, int>)), ("Custom", typeof(Custom)),
        ("List<Custom>", typeof(List<Custom>)), ("List<Plain>", typeof(List<Plain>)),
    ];

    private static readonly string[] Number = ["int", "long", "decimal", "double", "float", "int?", "long?", "decimal?", "double?", "float?"];
    private static readonly string[] Bool = ["bool", "bool?"];
    private static readonly string[] Date = ["DateTime", "DateTime?"];
    private static readonly string[] GuidOnly = ["Guid", "Guid?"];
    private static readonly string[] HiddenSingle =
        ["string", "int", "long", "decimal", "double", "float", "bool", "DateTime", "Guid", "int?", "long?", "decimal?",
         "double?", "float?", "bool?", "DateTime?", "Guid?"];
    private static readonly string[] HiddenStructured =
        ["List<string>", "List<int>", "List<Guid>", "List<TagItem>", "string[]", "Dictionary<string,string>",
         "Dictionary<string,int>", "Custom", "List<Custom>", "List<Plain>"];

    // Interface -> the type ids it accepts.
    public static readonly Dictionary<FieldInterface, string[]> Accepted = new()
    {
        [FieldInterface.Text] = ["string", "Guid", "Guid?"],
        [FieldInterface.Textarea] = ["string"], [FieldInterface.RichText] = ["string"],
        [FieldInterface.Markdown] = ["string"], [FieldInterface.Code] = ["string"], [FieldInterface.Slug] = ["string"],
        [FieldInterface.Email] = ["string"], [FieldInterface.Url] = ["string"], [FieldInterface.Password] = ["string"],
        [FieldInterface.Color] = ["string"], [FieldInterface.Phone] = ["string"],
        [FieldInterface.Number] = Number, [FieldInterface.Slider] = Number, [FieldInterface.Rating] = Number,
        [FieldInterface.Boolean] = Bool, [FieldInterface.Checkbox] = Bool,
        [FieldInterface.Date] = Date, [FieldInterface.Time] = Date, [FieldInterface.DateTime] = Date,
        [FieldInterface.Select] = ["string"], [FieldInterface.Radio] = ["string"],
        [FieldInterface.MultiSelect] = ["List<string>"], [FieldInterface.CheckboxGroup] = ["List<string>"],
        [FieldInterface.Tags] = ["List<TagItem>"],
        [FieldInterface.Json] = ["string"],
        [FieldInterface.KeyValue] = ["Dictionary<string,string>"],
        [FieldInterface.Repeater] = ["List<Custom>"],
        [FieldInterface.File] = GuidOnly, [FieldInterface.Image] = GuidOnly, [FieldInterface.Uuid] = GuidOnly,
        [FieldInterface.Files] = ["List<Guid>"],
        [FieldInterface.Hidden] = [.. HiddenSingle, "byte[]", .. HiddenStructured],
        [FieldInterface.Divider] = HiddenSingle,
    };

    public static readonly FieldInterface[] WithOptions =
        [FieldInterface.Select, FieldInterface.Radio, FieldInterface.MultiSelect, FieldInterface.CheckboxGroup];
}
