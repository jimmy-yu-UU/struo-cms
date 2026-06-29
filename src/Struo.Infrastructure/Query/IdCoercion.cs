namespace Struo.Infrastructure.Query;

/// <summary>
/// PK/FK-type-aware value coercion. Handles <see cref="Guid"/> (not <see cref="IConvertible"/>,
/// so <see cref="Convert.ChangeType(object, Type)"/> throws for it) in addition to the
/// IConvertible types used by the existing long-keyed collections.
/// </summary>
public static class IdCoercion
{
    public static object? Coerce(object? value, Type targetType)
    {
        if (value is null) return null;
        var underlying = Nullable.GetUnderlyingType(targetType) ?? targetType;
        if (underlying.IsInstanceOfType(value)) return value;
        if (underlying == typeof(Guid))
            return value is Guid g ? g : Guid.Parse(value.ToString()!);
        try { return Convert.ChangeType(value, underlying); }
        catch (Exception ex) when (ex is InvalidCastException or FormatException or OverflowException)
        { return value; }
    }
}
