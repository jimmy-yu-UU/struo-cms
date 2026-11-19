using System.Collections.Frozen;
using System.Reflection;
using SqlSugar;

namespace Struo.Infrastructure.Metadata;

/// <summary>
/// Which properties map to a NOT NULL column that a null can still reach: a writable, mapped, non-key
/// property whose CLR type holds null and whose column <see cref="ClrNullability.ColumnIsNullable"/> leaves
/// NOT NULL. The sets are built once at startup and the write path reads them.
/// </summary>
internal static class NotNullColumns
{
    internal static IReadOnlySet<string> PropertyNames(Type entityType) =>
        entityType.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(IsNotNullReference)
            .Select(p => p.Name)
            .ToFrozenSet(StringComparer.Ordinal);

    /// <summary>
    /// The NOT NULL properties among <paramref name="properties"/>, each with whether a new instance of
    /// <paramref name="entityType"/> holds null for it until a request sets it.
    /// </summary>
    internal static IReadOnlyDictionary<PropertyInfo, bool> WithStartsNull(
        Type entityType, IEnumerable<PropertyInfo> properties)
    {
        var fresh = Activator.CreateInstance(entityType)!;
        return properties
            .Where(IsNotNullReference)
            .ToFrozenDictionary(p => p, p => p.GetValue(fresh) is null);
    }

    internal static bool IsNotNullReference(PropertyInfo property) =>
        !property.PropertyType.IsValueType
        && property.CanWrite
        && property.GetCustomAttribute<SugarColumn>() is not { IsIgnore: true } and not { IsPrimaryKey: true }
        && !ClrNullability.ColumnIsNullable(property);
}
