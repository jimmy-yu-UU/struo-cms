// src/Struo.Api/GraphQl/SchemaTypeMapper.cs
using System.Globalization;
using Struo.Domain.Metadata.Enums;

namespace Struo.Api.GraphQl;

/// <summary>
/// Pure mapping from CMS metadata to GraphQL SDL type strings and schema names.
/// No HotChocolate or DB dependency — this is the single source of truth for how a
/// FieldInterface (and, for numbers, its CLR type) becomes a GraphQL type.
/// </summary>
public static class SchemaTypeMapper
{
    public static readonly IReadOnlySet<FieldInterface> Excluded =
        new HashSet<FieldInterface> { FieldInterface.Hidden, FieldInterface.Divider, FieldInterface.Password };

    public static bool IsExcluded(FieldInterface iface) => Excluded.Contains(iface);

    public static string TypeName(string collection) => Pascal(collection);
    public static string SingleFieldName(string collection) => Camel(collection);
    public static string ListFieldName(string collection) => Pluralise(Camel(collection));
    public static string RepeaterItemTypeName(string collection, string field) => Pascal(collection) + Pascal(field) + "Item";

    public static string CreateFieldName(string collection) => "create" + Pascal(collection);
    public static string UpdateFieldName(string collection) => "update" + Pascal(collection);
    public static string DeleteFieldName(string collection) => "delete" + Pascal(collection);
    public static string CreateInputName(string collection) => Pascal(collection) + "CreateInput";
    public static string UpdateInputName(string collection) => Pascal(collection) + "UpdateInput";

    /// <summary>
    /// Returns the nullable SDL type string for a scalar/list field, or <c>null</c> when the
    /// interface is excluded (Hidden/Divider/Password) or is a named-type interface
    /// (Tags/Repeater) that the schema builder handles separately.
    /// </summary>
    public static string? ScalarSdl(FieldInterface iface, Type? clrType) => iface switch
    {
        FieldInterface.Hidden or FieldInterface.Divider or FieldInterface.Password => null,
        FieldInterface.Tags or FieldInterface.Repeater => null,

        FieldInterface.Text or FieldInterface.Textarea or FieldInterface.RichText or FieldInterface.Markdown
            or FieldInterface.Code or FieldInterface.Slug or FieldInterface.Email or FieldInterface.Url
            or FieldInterface.Color or FieldInterface.Phone or FieldInterface.Select or FieldInterface.Radio => "String",

        FieldInterface.Number or FieldInterface.Slider or FieldInterface.Rating => NumberSdl(clrType),

        FieldInterface.Boolean or FieldInterface.Checkbox => "Boolean",
        FieldInterface.Date => "Date",
        FieldInterface.Time => "String",
        FieldInterface.DateTime => "DateTime",

        FieldInterface.MultiSelect or FieldInterface.CheckboxGroup => "[String!]",
        FieldInterface.Json or FieldInterface.KeyValue => "Any",

        FieldInterface.File or FieldInterface.Image => "ID",
        FieldInterface.Files => "[ID!]",
        FieldInterface.Uuid => "ID",
        _ => throw new NotSupportedException($"No GraphQL mapping for field interface '{iface}'.")
    };

    /// <summary>
    /// SDL for a writable scalar own-field in a create/update input, or <c>null</c> for interfaces
    /// deferred to Phase 8b.2 (MultiSelect/CheckboxGroup/Tags/Json/KeyValue/File/Image/Files/Repeater)
    /// or excluded entirely (Hidden/Divider/Password). This is the Phase 8b.1 writable subset — a
    /// deliberately narrower set than the read-side <see cref="ScalarSdl"/>.
    /// </summary>
    public static string? WritableScalarInputSdl(FieldInterface iface, Type? clrType) => iface switch
    {
        FieldInterface.Text or FieldInterface.Textarea or FieldInterface.RichText or FieldInterface.Markdown
            or FieldInterface.Code or FieldInterface.Slug or FieldInterface.Email or FieldInterface.Url
            or FieldInterface.Color or FieldInterface.Phone or FieldInterface.Select or FieldInterface.Radio
            or FieldInterface.Time => "String",
        FieldInterface.Number or FieldInterface.Slider or FieldInterface.Rating => NumberSdl(clrType),
        FieldInterface.Boolean or FieldInterface.Checkbox => "Boolean",
        FieldInterface.Date => "Date",
        FieldInterface.DateTime => "DateTime",
        FieldInterface.Uuid => "ID",
        _ => null,
    };

    private static string NumberSdl(Type? clrType)
    {
        var t = clrType is null ? null : Nullable.GetUnderlyingType(clrType) ?? clrType;
        if (t == typeof(int) || t == typeof(short) || t == typeof(byte)) return "Int";
        if (t == typeof(long)) return "Long";
        return "Float"; // decimal/double/float/unknown
    }

    private static string Pascal(string s)
    {
        if (string.IsNullOrEmpty(s)) return s;
        return char.ToUpper(s[0], CultureInfo.InvariantCulture) + s[1..];
    }

    private static string Camel(string s)
    {
        if (string.IsNullOrEmpty(s)) return s;
        return char.ToLower(s[0], CultureInfo.InvariantCulture) + s[1..];
    }

    // Simple, predictable English pluralisation (documented so hosts can rely on it).
    private static string Pluralise(string s)
    {
        if (s.EndsWith("y", StringComparison.Ordinal) && s.Length > 1 && !"aeiou".Contains(s[^2]))
            return s[..^1] + "ies";
        if (s.EndsWith("s", StringComparison.Ordinal) || s.EndsWith("x", StringComparison.Ordinal) ||
            s.EndsWith("z", StringComparison.Ordinal) || s.EndsWith("ch", StringComparison.Ordinal) ||
            s.EndsWith("sh", StringComparison.Ordinal))
            return s + "es";
        return s + "s";
    }
}
