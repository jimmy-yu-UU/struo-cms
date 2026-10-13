using System.Globalization;
using Struo.Domain.Query;

namespace Struo.Infrastructure.Query;

internal static class AggregateValueNormalizer
{
    public static object? Normalize(AggregateOp op, Type propertyType, object? raw)
    {
        if (raw is null or DBNull) return null;
        var target = Nullable.GetUnderlyingType(propertyType) ?? propertyType;
        return op switch
        {
            AggregateOp.Count => Convert.ToInt64(raw, CultureInfo.InvariantCulture),
            AggregateOp.Avg => Convert.ToDouble(raw, CultureInfo.InvariantCulture),
            AggregateOp.Sum => Sum(target, raw),
            _ => ToType(target, raw),
        };
    }

    private static object Sum(Type target, object raw)
    {
        if (target == typeof(decimal)) return Convert.ToDecimal(raw, CultureInfo.InvariantCulture);
        if (target == typeof(double) || target == typeof(float)) return Convert.ToDouble(raw, CultureInfo.InvariantCulture);
        return Convert.ToInt64(raw, CultureInfo.InvariantCulture);
    }

    private static object ToType(Type target, object raw) =>
        (raw is string text ? ParseText(target, text) : null)
        ?? FromTemporal(target, raw)
        ?? (raw.GetType() == target ? raw : Convert.ChangeType(raw, target, CultureInfo.InvariantCulture));

    private static object? ParseText(Type target, string text)
    {
        if (target == typeof(DateTime)) return DateTime.Parse(text, CultureInfo.InvariantCulture);
        if (target == typeof(DateOnly)) return DateOnly.Parse(text, CultureInfo.InvariantCulture);
        if (target == typeof(TimeOnly)) return TimeOnly.Parse(text, CultureInfo.InvariantCulture);
        return null;
    }

    // Npgsql returns a `date`/`time` column's min/max as a DateTime (midnight- or epoch-date-anchored),
    // and Convert.ChangeType cannot turn a DateTime into DateOnly/TimeOnly. A zone-aware column's
    // min/max is an instant: SQL Server returns a DateTimeOffset (not convertible to DateTime either)
    // and Npgsql a Local DateTime, which would serialize with the host offset. Unspecified and Utc
    // values (plain columns) are returned as read.
    private static object? FromTemporal(Type target, object raw)
    {
        if (target == typeof(DateOnly) && raw is DateTime date) return DateOnly.FromDateTime(date);
        if (target == typeof(TimeOnly) && raw is DateTime time) return TimeOnly.FromDateTime(time);
        if (target != typeof(DateTime)) return null;
        return raw switch
        {
            DateTimeOffset offset => offset.UtcDateTime,
            DateTime { Kind: DateTimeKind.Local } local => local.ToUniversalTime(),
            _ => null,
        };
    }
}
