using System.Reflection;
using SqlSugar;
using Struo.Domain.Metadata;

namespace Struo.Infrastructure.Metadata;

/// <summary>
/// The property lookups behind the scan-time key checks: a key property by name, and the properties
/// that carry <c>[SugarColumn(IsPrimaryKey = true)]</c>.
/// </summary>
internal static class KeyLookup
{
    public const string DefaultPrimaryKeyName = "Id";

    private const BindingFlags PublicInstance = BindingFlags.Public | BindingFlags.Instance;

    /// <summary>The public instance property called <paramref name="name"/>, spelled exactly, or null.</summary>
    public static PropertyInfo? Find(Type type, string name) =>
        Array.Find(type.GetProperties(PublicInstance), p => string.Equals(p.Name, name, StringComparison.Ordinal));

    /// <summary>The property <see cref="Find"/> returns, or a <see cref="MetadataException"/> when none.</summary>
    public static PropertyInfo Require(Type type, string name) =>
        Find(type, name) ?? throw new MetadataException(Absence(type, name));

    /// <summary>
    /// Why <paramref name="holder"/> has no property called <paramref name="name"/>; names the property
    /// that differs only in case when there is one. <paramref name="kind"/> says what the name stands for;
    /// <paramref name="resolution"/>, when given, says how the declaration led to <paramref name="holder"/>.
    /// </summary>
    public static string Absence(Type holder, string name, string kind = "key", string? resolution = null)
    {
        var label = Label(holder, resolution);
        return NearMiss(holder, name) is { } nearMiss
            ? $"{label} has no property '{name}'; it has '{nearMiss.Name}' — " +
              $"{kind} names are case-sensitive. Write the {kind} as '{nearMiss.Name}'."
            : $"{label} has no property with that name. " +
              $"Name a property of {holder.Name} that holds the {kind}, or add '{name}' to {holder.Name}.";
    }

    /// <summary>
    /// The name of <paramref name="holder"/>, followed by <paramref name="resolution"/> in parentheses when
    /// the declaration led to it by name.
    /// </summary>
    public static string Label(Type holder, string? resolution) =>
        resolution is null ? holder.Name : $"{holder.Name} ({resolution})";

    private static PropertyInfo? NearMiss(Type type, string name) =>
        Array.Find(
            type.GetProperties(PublicInstance),
            p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// True when the property is marked <c>[SugarColumn(IsIgnore = true)]</c> on its own declaration: it has
    /// no column.
    /// </summary>
    public static bool HasNoColumn(PropertyInfo property) => SugarColumnsOf(property).Any(a => a.IsIgnore);

    // [SugarColumn] on the property itself, without inheritance, as SqlSugar reads it.
    private static IEnumerable<SugarColumn> SugarColumnsOf(PropertyInfo property) =>
        property.GetCustomAttributes(typeof(SugarColumn), inherit: false).OfType<SugarColumn>();

    /// <summary>
    /// The properties of <paramref name="type"/> that carry <c>[SugarColumn(IsPrimaryKey = true)]</c> on
    /// their own declaration, read without inheritance as SqlSugar does. Why:
    /// docs/ai/decisions/relation-key-and-primary-key-declarations.md
    /// </summary>
    public static List<PropertyInfo> PrimaryKeyProperties(Type type) =>
        type.GetProperties(PublicInstance)
            .Where(p => SugarColumnsOf(p).Any(a => a.IsPrimaryKey))
            .ToList();
}
