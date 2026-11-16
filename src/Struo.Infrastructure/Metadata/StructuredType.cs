namespace Struo.Infrastructure.Metadata;

/// <summary>
/// Tells a single-value property type from a structured one. A <c>[CmsField]</c> property of a
/// structured type is stored as a JSON column; <c>ColumnDeclarationCheck</c> and
/// <c>SqlSugarClientFactory</c> both read this definition.
/// </summary>
internal static class StructuredType
{
    /// <summary>
    /// True unless the type, after unwrapping <see cref="Nullable{T}"/>, is a primitive, an enum,
    /// <see cref="string"/>, <see cref="decimal"/>, <see cref="Guid"/>, a date or time type, or
    /// <c>byte[]</c>.
    /// </summary>
    internal static bool Is(Type propertyType) => !IsSingleValue(Nullable.GetUnderlyingType(propertyType) ?? propertyType);

    private static bool IsSingleValue(Type type) =>
        type.IsPrimitive || type.IsEnum || SingleValueTypes.Contains(type);

    private static readonly HashSet<Type> SingleValueTypes =
    [
        typeof(string), typeof(decimal), typeof(Guid), typeof(DateTime), typeof(DateTimeOffset),
        typeof(DateOnly), typeof(TimeOnly), typeof(TimeSpan), typeof(byte[]),
    ];
}
