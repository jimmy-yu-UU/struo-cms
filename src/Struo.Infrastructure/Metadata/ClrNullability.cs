using System.Reflection;
using SqlSugar;

namespace Struo.Infrastructure.Metadata;

/// <summary>
/// Which properties get a nullable column. The column convention applies <see cref="GivesNullableColumn"/>
/// to every property, the scanner reads it to reject a contradicting <c>IsNullable = false</c>, and the
/// write path derives the NOT NULL columns from <see cref="ColumnIsNullable"/>.
/// </summary>
internal static class ClrNullability
{
    /// <summary>
    /// True for a <see cref="Nullable{T}"/> and for a <see cref="string"/> the compiler annotated as
    /// nullable. A string in nullable-oblivious code is not.
    /// </summary>
    internal static bool GivesNullableColumn(PropertyInfo property)
    {
        if (Nullable.GetUnderlyingType(property.PropertyType) is not null) return true;
        if (property.PropertyType != typeof(string)) return false;

        // NullabilityInfoContext is not thread-safe, so each call uses its own instance.
        return new NullabilityInfoContext().Create(property).WriteState == NullabilityState.Nullable;
    }

    /// <summary>
    /// True when the column of the property accepts null: its type gives a nullable column, or it declares
    /// <c>[SugarColumn(IsNullable = true)]</c>.
    /// </summary>
    internal static bool ColumnIsNullable(PropertyInfo property) =>
        GivesNullableColumn(property) || property.GetCustomAttribute<SugarColumn>() is { IsNullable: true };
}
