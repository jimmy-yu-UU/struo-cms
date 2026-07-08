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
