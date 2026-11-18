using System.Collections.Frozen;
using Struo.Domain.Metadata.Enums;
using Struo.Domain.Metadata.Models;

namespace Struo.Infrastructure.Metadata;

/// <summary>
/// The CLR property types each <see cref="FieldInterface"/> accepts. <c>ColumnDeclarationCheck</c> looks a
/// <c>[CmsField]</c> property up here at startup; <c>InterfacePropertyTypesTests</c> pins that every enum
/// member has an entry. A type is matched exactly: <c>List&lt;string&gt;</c> is not <c>string[]</c> or
/// <c>IList&lt;string&gt;</c>.
/// </summary>
internal static class InterfacePropertyTypes
{
    private static readonly Type[] Texts = [typeof(string)];
    private static readonly Type[] Guids = [typeof(Guid), typeof(Guid?)];
    private static readonly Type[] Dates = [typeof(DateTime), typeof(DateTime?)];
    private static readonly Type[] Bools = [typeof(bool), typeof(bool?)];

    private static readonly Type[] Numbers =
    [
        typeof(int), typeof(int?), typeof(long), typeof(long?), typeof(decimal), typeof(decimal?),
        typeof(double), typeof(double?), typeof(float), typeof(float?),
    ];

    private static readonly Type[] HiddenSingles =
    [
        typeof(string), .. Numbers, .. Bools, .. Dates, .. Guids, typeof(byte[]),
    ];

    private static readonly Type[] DividerSingles = HiddenSingles.Where(t => t != typeof(byte[])).ToArray();

    // Interfaces whose accepted types are one fixed list; Hidden, Divider and Repeater are open-ended.
    private static readonly FrozenDictionary<FieldInterface, Type[]> Closed = BuildClosed().ToFrozenDictionary();

    private static IEnumerable<KeyValuePair<FieldInterface, Type[]>> BuildClosed()
    {
        FieldInterface[] texts =
        [
            FieldInterface.Textarea, FieldInterface.RichText, FieldInterface.Markdown, FieldInterface.Code,
            FieldInterface.Slug, FieldInterface.Email, FieldInterface.Url, FieldInterface.Password,
            FieldInterface.Color, FieldInterface.Phone, FieldInterface.Select, FieldInterface.Radio,
            FieldInterface.Json,
        ];
        foreach (var member in texts) yield return new(member, Texts);

        yield return new(FieldInterface.Text, [typeof(string), .. Guids]);
        foreach (var member in new[] { FieldInterface.Number, FieldInterface.Slider, FieldInterface.Rating })
            yield return new(member, Numbers);
        foreach (var member in new[] { FieldInterface.Boolean, FieldInterface.Checkbox })
            yield return new(member, Bools);
        foreach (var member in new[] { FieldInterface.Date, FieldInterface.Time, FieldInterface.DateTime })
            yield return new(member, Dates);
        foreach (var member in new[] { FieldInterface.MultiSelect, FieldInterface.CheckboxGroup })
            yield return new(member, [typeof(List<string>)]);
        foreach (var member in new[] { FieldInterface.File, FieldInterface.Image, FieldInterface.Uuid })
            yield return new(member, Guids);

        yield return new(FieldInterface.Tags, [typeof(List<TagItem>)]);
        yield return new(FieldInterface.KeyValue, [typeof(Dictionary<string, string>)]);
        yield return new(FieldInterface.Files, [typeof(List<Guid>)]);
    }

    /// <summary>True when the table has an entry for <paramref name="member"/>.</summary>
    internal static bool Has(FieldInterface member) =>
        Closed.ContainsKey(member) || member is FieldInterface.Hidden or FieldInterface.Divider or FieldInterface.Repeater;

    internal static bool Accepts(FieldInterface member, Type propertyType)
    {
        if (Closed.TryGetValue(member, out var types)) return types.Contains(propertyType);
        return member switch
        {
            FieldInterface.Hidden => HiddenSingles.Contains(propertyType) || StructuredType.Is(propertyType),
            FieldInterface.Divider => DividerSingles.Contains(propertyType),
            FieldInterface.Repeater => IsChildList(propertyType),
            _ => throw new NotSupportedException($"No property-type entry for field interface '{member}'."),
        };
    }

    /// <summary>The interfaces that accept <paramref name="propertyType"/>, in declaration order.</summary>
    internal static IReadOnlyList<FieldInterface> Accepting(Type propertyType) =>
        Enum.GetValues<FieldInterface>().Where(m => Accepts(m, propertyType)).ToList();

    /// <summary>The accepted types of <paramref name="member"/> as a phrase for a message.</summary>
    internal static string Describe(FieldInterface member)
    {
        if (Closed.TryGetValue(member, out var types)) return JoinOr(types.Select(Display));
        return member switch
        {
            FieldInterface.Hidden => JoinOr([.. HiddenSingles.Select(Display), "a structured type such as List<T> or a class"]),
            FieldInterface.Divider => JoinOr(DividerSingles.Select(Display)),
            FieldInterface.Repeater => "List<TChild>, where TChild is a class with [CmsField] properties",
            _ => throw new NotSupportedException($"No property-type entry for field interface '{member}'."),
        };
    }

    /// <summary>A type as a fork author writes it: <c>int?</c>, <c>List&lt;Guid&gt;</c>, <c>byte[]</c>.</summary>
    internal static string Display(Type type) =>
        Nullable.GetUnderlyingType(type) is { } inner ? ColumnDeclarationCheck.TypeName(inner) + "?" : ColumnDeclarationCheck.TypeName(type);

    internal static string JoinOr(IEnumerable<string> items)
    {
        var list = items.ToList();
        return list.Count < 2 ? string.Concat(list) : string.Join(", ", list[..^1]) + " or " + list[^1];
    }

    // Whether the type could be a Repeater's list; BuildRepeaterChildFields reports why a list does not
    // qualify, so a non-list type there keeps that message.
    private static bool IsChildList(Type type) =>
        type.IsGenericType && type.GetGenericTypeDefinition() == typeof(List<>) &&
        type.GetGenericArguments()[0] is { IsClass: true } child && child != typeof(string);
}
