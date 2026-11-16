using System.Reflection;

namespace Struo.Infrastructure.Metadata;

/// <summary>
/// Which properties get a nullable column from their CLR type alone. The column convention applies
/// it to every property and the scanner reads it to reject a contradicting <c>IsNullable = false</c>.
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
}
