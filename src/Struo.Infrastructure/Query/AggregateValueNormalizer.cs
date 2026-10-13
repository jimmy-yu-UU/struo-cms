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

    private static object ToType(Type target, object raw)
    {
        if (raw is string s)
        {
            if (target == typeof(DateTime)) return DateTime.Parse(s, CultureInfo.InvariantCulture);
            if (target == typeof(DateOnly)) return DateOnly.Parse(s, CultureInfo.InvariantCulture);
            if (target == typeof(TimeOnly)) return TimeOnly.Parse(s, CultureInfo.InvariantCulture);
        }
        // Npgsql returns a `date`/`time` column's min/max aggregate as a DateTime (midnight-anchored
        // for `date`, epoch-date-anchored for `time`), never as DateOnly/TimeOnly directly.
        // Convert.ChangeType has no built-in conversion between DateTime and either type and throws
        // InvalidCastException, so it must be special-cased here before the generic fallback below.
        if (raw is DateTime dt)
        {
            if (target == typeof(DateOnly)) return DateOnly.FromDateTime(dt);
            if (target == typeof(TimeOnly)) return TimeOnly.FromDateTime(dt);
        }
        // A zone-aware column's min/max is an instant: SQL Server returns it as a DateTimeOffset and
        // Npgsql as a Local DateTime. Convert.ChangeType cannot turn a DateTimeOffset into a DateTime
        // (InvalidCastException), and a Local value would serialize with the host offset. Unspecified and
        // Utc values (plain columns) are returned as read.
        if (target == typeof(DateTime))
        {
            if (raw is DateTimeOffset offset) return offset.UtcDateTime;
            if (raw is DateTime { Kind: DateTimeKind.Local } local) return local.ToUniversalTime();
        }
        return raw.GetType() == target ? raw : Convert.ChangeType(raw, target, CultureInfo.InvariantCulture);
    }
}
