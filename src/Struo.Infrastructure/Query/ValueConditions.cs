// src/Struo.Infrastructure/Query/ValueConditions.cs
using System.Collections;
using System.Globalization;
using SqlSugar;
using Struo.Application.Query;
using Struo.Domain.Query;
using Struo.Infrastructure.Persistence;

namespace Struo.Infrastructure.Query;

/// <summary>The column a filter value is compared with: its database name, the name shown in errors, and its CLR type.</summary>
internal readonly record struct ValueTarget(
    string Column, string Label, Type ClrType, SqlSugar.DbType DbType, bool UtcInstant = false)
{
    public Type Underlying => Nullable.GetUnderlyingType(ClrType) ?? ClrType;
}

/// <summary>The terms a value operator contributes to an <c>_or</c> group, plus a column that proves the group empty.</summary>
internal sealed record OrTerms(IReadOnlyList<ConditionalModel> Models, string? NeverMatchesColumn = null);

/// <summary>
/// The one place a filter or id value becomes a SqlSugar condition, and the only place in <c>src/</c>
/// that names <see cref="ConditionalType.In"/> or <see cref="ConditionalType.NotIn"/>. Each value is
/// parsed to the column's CLR type with the invariant culture and rendered again from the parsed value,
/// so text the caller wrote never reaches the SQL of a numeric, boolean, Guid or date column. A string
/// column takes a set as a group of parameterised equalities. Text that does not parse is a
/// <see cref="QueryException"/> naming the column, never the values. Measurements, the SqlSugar traps
/// and the unverified parts (SQL Server) are in docs/ai/decisions/query-values-are-parsed-and-bound.md.
/// </summary>
internal static class ValueConditions
{
    private const string Placeholder = "v";

    private static readonly HashSet<Type> Supported =
    [
        typeof(Guid), typeof(int), typeof(long), typeof(short), typeof(byte), typeof(bool), typeof(decimal),
        typeof(double), typeof(float), typeof(DateTime), typeof(DateTimeOffset), typeof(string),
    ];

    public static IConditionalModel? Build(ValueTarget t, QueryOperator op, object? value) => op switch
    {
        QueryOperator.Null or QueryOperator.NNull => Plain(t, op, null, null),
        QueryOperator.In => SetCondition(t, true, value),
        QueryOperator.Nin => SetCondition(t, false, value),
        QueryOperator.Eq or QueryOperator.Neq or QueryOperator.Gt or QueryOperator.Gte
            or QueryOperator.Lt or QueryOperator.Lte => Comparison(t, op, value),
        _ => Plain(t, op, ToText(value), TextOperatorTypeName(t.Underlying)),
    };

    /// <summary>Terms for an <c>_or</c> group: every term is one <see cref="ConditionalModel"/> joined with OR.</summary>
    public static OrTerms OrMembers(ValueTarget t, QueryOperator op, object? value)
    {
        if (op is not (QueryOperator.In or QueryOperator.Nin))
            return new OrTerms([(ConditionalModel)Build(t, op, value)!]);

        var include = op == QueryOperator.In;
        if (!include && t.Underlying == typeof(string))
            throw new QueryException(
                $"'_nin' on the text field '{t.Label}' cannot be used inside '_or'; put it at the top level or inside '_and'.");

        var values = SetValues(t, include, value);
        if (values.Count == 0)
            return include ? new OrTerms([], t.Column) : new OrTerms([IsNullTest(t.Column), IsNotNullTest(t.Column)]);
        return include && t.Underlying == typeof(string)
            ? new OrTerms(values.Select(v => BoundEquality(t.Column, ConditionalType.Equal, v)).ToList())
            : new OrTerms([ListModel(t, include, values)]);
    }

    /// <summary>
    /// The same condition for a value that comes from the application, not from a request: every element
    /// must already be of the column's CLR type (a whole number of any width counts for a whole-number
    /// column). A value that is not is a bug in the caller, so it is an <see cref="InvalidOperationException"/>
    /// (a 500) and never reaches SQL; a value a request supplies must be checked by the caller first.
    /// </summary>
    public static IConditionalModel? BuildInternal(ValueTarget t, QueryOperator op, object? value)
    {
        foreach (var element in FilterValueList.Elements(value))
            if (element is null || !IsTyped(t.Underlying, element))
                throw new InvalidOperationException(
                    $"A value for column '{t.Column}' is not a {Describe(t.Underlying)}; the caller must pass typed values.");
        try
        {
            return Build(t, op, value);
        }
        catch (QueryException ex)
        {
            throw new InvalidOperationException(
                $"A value for column '{t.Column}' is not a {Describe(t.Underlying)}; the caller must pass typed values.", ex);
        }
    }

    public static ConditionalCollections NeverMatches(string column) => new()
    {
        ConditionalList =
        [
            new(WhereType.And, IsNullTest(column)),
            new(WhereType.And, IsNotNullTest(column)),
        ],
    };

    private static string? TypeName(Type clrType)
    {
        if (clrType.IsEnum) return TypeName(Enum.GetUnderlyingType(clrType));
        if (clrType == typeof(Guid)) return "guid";
        if (clrType == typeof(int)) return "int";
        if (clrType == typeof(long)) return "long";
        if (clrType == typeof(short)) return "short";
        if (clrType == typeof(byte)) return "byte";
        if (clrType == typeof(bool)) return "bool";
        if (clrType == typeof(DateTime)) return "datetime";
        if (clrType == typeof(DateTimeOffset)) return "datetimeoffset";
        if (clrType == typeof(decimal)) return "decimal";
        if (clrType == typeof(double)) return "double";
        if (clrType == typeof(float)) return "float";
        return null;
    }

    // A text operator (`_contains` and the like) reads the value as text for every column; byte and enum
    // columns carry no type name there.
    private static string? TextOperatorTypeName(Type t) => t.IsEnum || t == typeof(byte) ? null : TypeName(t);

    private static bool IsSupported(Type t) => t.IsEnum || Supported.Contains(t);

    private static bool IsTyped(Type column, object value) =>
        column.IsInstanceOfType(value) || (IsWholeNumber(column) && IsWholeNumber(value.GetType()));

    private static bool IsWholeNumber(Type t) =>
        t == typeof(long) || t == typeof(int) || t == typeof(short) || t == typeof(byte);

    // ── operators ────────────────────────────────────────────────────────

    private static IConditionalModel? SetCondition(ValueTarget t, bool include, object? value)
    {
        var values = SetValues(t, include, value);
        if (values.Count == 0) return include ? NeverMatches(t.Column) : null;
        if (t.Underlying != typeof(string)) return ListModel(t, include, values);

        var type = include ? ConditionalType.Equal : ConditionalType.NoEqual;
        return new ConditionalCollections
        {
            ConditionalList = values
                .Select((v, i) => new KeyValuePair<WhereType, ConditionalModel>(
                    i == 0 || !include ? WhereType.And : WhereType.Or, BoundEquality(t.Column, type, v)))
                .ToList(),
        };
    }

    private static ConditionalModel Comparison(ValueTarget t, QueryOperator op, object? value)
    {
        var type = t.Underlying;
        if (value is null || !IsSupported(type) || type == typeof(string))
            return Plain(t, op, ToText(value), type == typeof(string) ? null : TypeName(type));
        if (value is IEnumerable and not string)
            throw Invalid(t, "a single value");
        var text = Format(t, value, forSet: false) ?? throw Invalid(t, Describe(type));
        return Plain(t, op, text, TypeNameFor(t));
    }

    // The parsed, re-rendered elements of a set (distinct for a text column, which keeps its text as it is).
    private static List<string> SetValues(ValueTarget t, bool include, object? value)
    {
        var type = t.Underlying;
        if (!IsSupported(type))
            throw new QueryException($"'{(include ? "_in" : "_nin")}' is not supported on field '{t.Label}'.");

        var seen = new HashSet<string>(StringComparer.Ordinal);
        var result = new List<string>();
        foreach (var element in FilterValueList.Elements(value))
        {
            if (element is null or (IEnumerable and not string))
                throw new QueryException($"Field '{t.Label}' takes a list of plain values; null and nested lists are not allowed.");
            var text = type == typeof(string)
                ? element as string ?? throw new QueryException($"Field '{t.Label}' takes a list of text values.")
                : Format(t, element, forSet: true) ?? throw Invalid(t, Describe(type));
            // A text value repeated would be a second parameter for the same word; a listed number repeated is harmless.
            if (type != typeof(string) || seen.Add(text)) result.Add(text);
        }
        return result;
    }

    private static ConditionalModel ListModel(ValueTarget t, bool include, List<string> values) => new()
    {
        FieldName = t.Column,
        ConditionalType = include ? ConditionalType.In : ConditionalType.NotIn,
        FieldValue = string.Join(",", values),
        CSharpTypeName = TypeNameFor(t),
    };

    // A fresh instance for every call: SqlSugar numbers a group's members by their position in the list.
    // The value is bound through FieldValueConvertFunc and FieldValue holds a fixed word, because SqlSugar
    // reads the texts "null" and "[null]" in FieldValue of an Equal as a null test and rewrites the second
    // in place.
    private static ConditionalModel BoundEquality(string column, ConditionalType type, string value) => new()
    {
        FieldName = column,
        ConditionalType = type,
        FieldValue = Placeholder,
        FieldValueConvertFunc = _ => value,
    };

    private static ConditionalModel Plain(ValueTarget t, QueryOperator op, string? value, string? typeName) => new()
    {
        FieldName = t.Column,
        ConditionalType = Map(op),
        FieldValue = value,
        CSharpTypeName = typeName,
    };

    private static ConditionalModel IsNullTest(string column) =>
        new() { FieldName = column, ConditionalType = ConditionalType.EqualNull };

    private static ConditionalModel IsNotNullTest(string column) =>
        new() { FieldName = column, ConditionalType = ConditionalType.IsNot, FieldValue = null };

    private static ConditionalType Map(QueryOperator op) => op switch
    {
        QueryOperator.Eq => ConditionalType.Equal,
        QueryOperator.Neq => ConditionalType.NoEqual,
        QueryOperator.Contains => ConditionalType.Like,
        // SqlSugar's LikeLeft/LikeRight naming is the OPPOSITE of what the enum names suggest
        // (verified empirically against SqlSugarCore 5.1.4.215 on SQLite): LikeLeft renders
        // `value%` (wildcard trails, i.e. starts-with) and LikeRight renders `%value` (wildcard
        // leads, i.e. ends-with) — hence StartsWith below maps to LikeLeft, EndsWith to LikeRight.
        QueryOperator.StartsWith => ConditionalType.LikeLeft,
        QueryOperator.EndsWith => ConditionalType.LikeRight,
        QueryOperator.In => ConditionalType.In,
        QueryOperator.Nin => ConditionalType.NotIn,
        QueryOperator.Lt => ConditionalType.LessThan,
        QueryOperator.Lte => ConditionalType.LessThanOrEqual,
        QueryOperator.Gt => ConditionalType.GreaterThan,
        QueryOperator.Gte => ConditionalType.GreaterThanOrEqual,
        // Portable null checks. EqualNull renders pure "col IS NULL"; IsNot renders
        // pure "col IS NOT NULL". IsNullOrEmpty would emit "OR col = ''", which throws
        // a type error on PostgreSQL for non-text columns (bigint, timestamptz).
        QueryOperator.Null => ConditionalType.EqualNull,
        QueryOperator.NNull => ConditionalType.IsNot,
        _ => throw new InvalidOperationException($"Unmapped operator: {op}"),
    };

    // Convert.ToString(v, InvariantCulture) rather than v.ToString(): a bare ToString() on
    // decimal/double/DateTime renders using CultureInfo.CurrentCulture, so a comma-decimal
    // locale (e.g. de-DE) would emit "1,5" into what must be a dot-decimal SQL literal.
    private static string? ToText(object? value) => value switch
    {
        null => null,
        IEnumerable list when value is not string =>
            string.Join(",", list.Cast<object?>().Select(v => v is null ? null : Convert.ToString(v, CultureInfo.InvariantCulture))),
        _ => Convert.ToString(value, CultureInfo.InvariantCulture),
    };

    private static string? TypeNameFor(ValueTarget t)
    {
        var type = t.Underlying;
        return type == typeof(DateTime) && UsesInstantBinding(t) ? "datetimeoffset" : TypeName(type);
    }

    private static bool UsesInstantBinding(ValueTarget t) => t.UtcInstant && UtcInstantConverter.AppliesTo(t.DbType);

    private static QueryException Invalid(ValueTarget t, string expected) =>
        new($"Field '{t.Label}' needs {expected}.");

    private static string Describe(Type t)
    {
        if (t.IsEnum) return "a member name or number of the enum";
        if (t == typeof(Guid)) return "a UUID";
        if (t == typeof(bool)) return "true, false, 1 or 0";
        if (t == typeof(DateTime) || t == typeof(DateTimeOffset)) return "a date-time (ISO 8601)";
        if (t == typeof(decimal) || t == typeof(double) || t == typeof(float)) return "a number";
        return "an integer";
    }

    // ── per-type parsing; null = the value is not of the column's type ──

    private static string? Format(ValueTarget t, object value, bool forSet)
    {
        var type = t.Underlying;
        return type switch
        {
            _ when type == typeof(Guid) => ParseGuid(value),
            _ when type == typeof(int) => Integer(value, int.MinValue, int.MaxValue),
            _ when type == typeof(long) => Integer(value, long.MinValue, long.MaxValue),
            _ when type == typeof(short) => Integer(value, short.MinValue, short.MaxValue),
            _ when type == typeof(byte) => Integer(value, byte.MinValue, byte.MaxValue),
            _ when type == typeof(bool) => ParseBool(value, forSet),
            _ when type == typeof(decimal) => ParseDecimal(value),
            _ when type == typeof(double) => ParseDouble(value, float32: false),
            _ when type == typeof(float) => ParseDouble(value, float32: true),
            _ when type == typeof(DateTime) => ParseInstant(t, value),
            _ when type == typeof(DateTimeOffset) => ParseOffset(value),
            _ when type.IsEnum => ParseEnum(type, value),
            _ => null,
        };
    }

    private static string? ParseGuid(object value) => value switch
    {
        Guid g => g.ToString("D"),
        string s when Guid.TryParse(s, out var g) => g.ToString("D"),
        _ => null,
    };

    private static string? Integer(object value, long min, long max)
    {
        long? parsed = value switch
        {
            long or int or short or byte or sbyte or ushort or uint => Convert.ToInt64(value, CultureInfo.InvariantCulture),
            double d when double.IsFinite(d) && d == Math.Floor(d) && Math.Abs(d) < 9.2e18 => (long)d,
            decimal m when m == decimal.Truncate(m) && Math.Abs(m) < 9.2e18m => (long)m,
            string s when long.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) => n,
            _ => null,
        };
        return parsed is { } v && v >= min && v <= max ? v.ToString(CultureInfo.InvariantCulture) : null;
    }

    // A set renders 1/0 (SqlSugar writes the text as it is, and every backend reads 1/0 as a boolean);
    // a comparison renders True/False (SqlSugar converts the text to a bool parameter itself).
    private static string? ParseBool(object value, bool forSet)
    {
        bool? parsed = value switch
        {
            bool b => b,
            long n when n is 0 or 1 => n == 1,
            int n when n is 0 or 1 => n == 1,
            string s when s.Trim() is "1" => true,
            string s when s.Trim() is "0" => false,
            string s when bool.TryParse(s, out var b) => b,
            _ => null,
        };
        if (parsed is not { } v) return null;
        if (forSet) return v ? "1" : "0";
        return v ? "True" : "False";
    }

    private static string? ParseDecimal(object value)
    {
        try
        {
            decimal? parsed = value switch
            {
                decimal m => m,
                long or int or short or byte => Convert.ToDecimal(value, CultureInfo.InvariantCulture),
                double d when double.IsFinite(d) => (decimal)d,
                float f when float.IsFinite(f) => (decimal)f,
                string s when decimal.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var m) => m,
                _ => null,
            };
            return parsed?.ToString(CultureInfo.InvariantCulture);
        }
        catch (OverflowException)
        {
            return null;
        }
    }

    private static string? ParseDouble(object value, bool float32)
    {
        double? parsed = value switch
        {
            double d => d,
            float f => f,
            decimal m => (double)m,
            long or int or short or byte => Convert.ToDouble(value, CultureInfo.InvariantCulture),
            string s when double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) => d,
            _ => null,
        };
        if (parsed is not { } v || !double.IsFinite(v)) return null;
        if (!float32) return v.ToString("R", CultureInfo.InvariantCulture);
        var single = (float)v;
        return float.IsFinite(single) ? single.ToString("R", CultureInfo.InvariantCulture) : null;
    }

    private static string? ParseEnum(Type enumType, object value)
    {
        object? parsed = value switch
        {
            string s when long.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) => ToEnum(enumType, n),
            string s when s.Contains(',') => null,
            string s when Enum.TryParse(enumType, s.Trim(), ignoreCase: true, out var e) => e,
            long or int or short or byte => ToEnum(enumType, Convert.ToInt64(value, CultureInfo.InvariantCulture)),
            _ when value.GetType() == enumType => value,
            _ => null,
        };
        return parsed is not null && Enum.IsDefined(enumType, parsed)
            ? Convert.ToInt64(parsed, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture)
            : null;
    }

    private static object? ToEnum(Type enumType, long number)
    {
        try
        {
            return Enum.ToObject(enumType, number);
        }
        catch (OverflowException)
        {
            return null;
        }
    }

    // A filter value on a DateTime column is an instant: a trailing Z or offset names it and a value
    // without one is UTC, which is how every DateTime the product writes is kept. A comparison or set
    // operator therefore compares instants, never the host's local time. Where the column is bound as
    // an instant (a TimestampWithTimeZone column on PostgreSQL and SQL Server) the value goes out as
    // DateTimeOffset at +00:00, which neither the session zone nor SQL Server's legacy datetime
    // rounding touches. Any other column gets the UTC wall clock with no zone: SQL Server's datetime
    // column rounds it to 1/300 s the way it rounds the stored value, so the text is an ISO 8601
    // form cut to milliseconds, which datetime reads under any language setting; the other backends
    // get Microsoft.Data.Sqlite's text form, which MySQL also reads as a datetime and PostgreSQL binds
    // as a zone-less timestamp (In/Nin render inline literals).
    private static string? ParseInstant(ValueTarget t, object value)
    {
        if (ToUtcInstant(value) is not { } instant) return null;
        if (UsesInstantBinding(t)) return new DateTimeOffset(instant).ToString("O", CultureInfo.InvariantCulture);
        var wall = t.DbType == SqlSugar.DbType.SqlServer
            ? WallClock(instant)
            : instant;
        var format = t.DbType == SqlSugar.DbType.SqlServer ? "yyyy-MM-ddTHH:mm:ss.fff" : "yyyy-MM-dd HH:mm:ss.FFFFFFF";
        return wall.ToString(format, CultureInfo.InvariantCulture);
    }

    private static readonly DateTime SqlServerDateTimeMax = new(9999, 12, 31, 23, 59, 59, 997, DateTimeKind.Utc);

    // SQL Server's datetime holds 1/300 s steps, so milliseconds are the finest text it takes. The
    // rounded value is clamped to datetime's largest value, 9999-12-31 23:59:59.997.
    private static DateTime WallClock(DateTime instant) =>
        new(Math.Min(SqlServerDateTimeMax.Ticks,
            (instant.Ticks + TimeSpan.TicksPerMillisecond / 2) / TimeSpan.TicksPerMillisecond
            * TimeSpan.TicksPerMillisecond), DateTimeKind.Utc);

    private static string? ParseOffset(object value) => value switch
    {
        DateTimeOffset o => o.ToString("O", CultureInfo.InvariantCulture),
        DateTime d => new DateTimeOffset(UtcInstantConverter.ToUtc(d)).ToString("O", CultureInfo.InvariantCulture),
        string s when DateTimeOffset.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var o) =>
            o.ToString("O", CultureInfo.InvariantCulture),
        _ => null,
    };

    private static DateTime? ToUtcInstant(object? value) => value switch
    {
        DateTime instant => UtcInstantConverter.ToUtc(instant),
        DateTimeOffset offset => offset.UtcDateTime,
        string text when DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal, out var parsed) => parsed.UtcDateTime,
        _ => null,
    };
}
