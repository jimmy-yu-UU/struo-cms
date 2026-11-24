using System.Reflection;
using SqlSugar;

namespace Struo.Infrastructure.Metadata;

/// <summary>
/// The property lookups behind the scan-time key checks: a key property by name, and the properties
/// that carry <c>[SugarColumn(IsPrimaryKey = true)]</c>.
/// </summary>
internal static class KeyLookup
{
    public const string DefaultPrimaryKeyName = "Id";

    private const BindingFlags PublicInstance = BindingFlags.Public | BindingFlags.Instance;

    /// <summary>The public instance property called <paramref name="name"/>, or null.</summary>
    public static PropertyInfo? Find(Type type, string name, bool caseSensitive = true) =>
        type.GetProperties(PublicInstance).FirstOrDefault(p => string.Equals(
            p.Name, name, caseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Why <paramref name="holder"/> has no property called <paramref name="key"/>; names the property
    /// that differs only in case when there is one.
    /// </summary>
    public static string Absence(Type holder, string key) =>
        Find(holder, key, caseSensitive: false) is { } nearMiss
            ? $"{holder.Name} has no property '{key}'; it has '{nearMiss.Name}' — key names are case-sensitive. " +
              $"Write the key as '{nearMiss.Name}'."
            : $"{holder.Name} has no property with that name. " +
              $"Name a property of {holder.Name} that holds the key, or add '{key}' to {holder.Name}.";

    /// <summary>
    /// The properties of <paramref name="type"/> that carry <c>[SugarColumn(IsPrimaryKey = true)]</c> on
    /// their own declaration. SqlSugar reads the attribute off the property itself
    /// (<c>GetCustomAttributes(typeof(SugarColumn), false)</c>): a key marked on a base class property is
    /// found when the property is inherited, and an override that drops the attribute carries no key.
    /// </summary>
    public static List<PropertyInfo> PrimaryKeyProperties(Type type) =>
        type.GetProperties(PublicInstance)
            .Where(p => p.GetCustomAttributes(typeof(SugarColumn), inherit: false).OfType<SugarColumn>().Any(a => a.IsPrimaryKey))
            .ToList();
}
