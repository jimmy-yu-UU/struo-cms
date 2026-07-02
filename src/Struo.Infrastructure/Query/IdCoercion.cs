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
        {
            if (value is Guid g) return g;
            // An empty/blank string is not a Guid; for a (typically nullable) id/FK it
            // means "no reference" — coerce to null rather than throwing on Guid.Parse("").
            var s = value.ToString();
            return string.IsNullOrWhiteSpace(s) ? null : Guid.Parse(s);
        }
        try { return Convert.ChangeType(value, underlying); }
        catch (Exception ex) when (ex is InvalidCastException or FormatException or OverflowException)
        { return value; }
    }
}
