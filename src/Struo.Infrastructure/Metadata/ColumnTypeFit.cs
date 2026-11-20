using System.Text;

namespace Struo.Infrastructure.Metadata;

/// <summary>
/// The closed table of explicit <c>ColumnDataType</c> names that contradict the property's CLR type and so
/// misbehave at run time. A name that is not in the table is the fork's own and passes.
/// </summary>
internal static class ColumnTypeFit
{
    internal readonly record struct Misfit(string Fix, string Why);

    private const string ZoneFix = "Declare [ColumnShape(ColumnShape.TimestampWithTimeZone)] and remove ColumnDataType";
    private const string ZoneWhy =
        "a column type alone leaves the value to be written and read in the host's local time, not as a UTC instant";
    private const string WidthWhy =
        "on SQL Server a facet of this column fails when a facet of a column of the property's own width ran first";

    private sealed record Rule(Type Clr, HashSet<string> ColumnTypes, Misfit Misfit);

    private static readonly Rule[] Rules =
    [
        new(typeof(DateTime), ["timestamptz", "timestamp with time zone", "datetimeoffset"], new(ZoneFix, ZoneWhy)),
        new(typeof(int), ["bigint"], new("Declare the property as long{?}, or remove ColumnDataType", WidthWhy)),
        new(typeof(int), ["smallint"], new("Remove ColumnDataType, or declare the column as int", WidthWhy)),
        new(typeof(long), ["int", "integer"], new("Declare the property as int{?}, or remove ColumnDataType", WidthWhy)),
        new(typeof(long), ["smallint"], new("Remove ColumnDataType, or declare the column as bigint", WidthWhy)),
    ];

    /// <summary>
    /// The contradiction between <paramref name="propertyType"/> (<c>Nullable&lt;T&gt;</c> counts as
    /// <c>T</c>) and the column type name, or null when the pair is not in the table.
    /// </summary>
    internal static Misfit? Find(Type propertyType, string? columnDataType)
    {
        if (string.IsNullOrWhiteSpace(columnDataType)) return null;

        var nullable = Nullable.GetUnderlyingType(propertyType) is not null;
        var single = Nullable.GetUnderlyingType(propertyType) ?? propertyType;
        var name = Normalize(columnDataType);

        foreach (var rule in Rules)
        {
            if (rule.Clr != single || !rule.ColumnTypes.Contains(name)) continue;
            return rule.Misfit with { Fix = rule.Misfit.Fix.Replace("{?}", nullable ? "?" : "", StringComparison.Ordinal) };
        }

        return null;
    }

    // Lower case, no parenthesised length or precision, one space between words, no surrounding space:
    // "Timestamp(3)  With Time Zone" becomes "timestamp with time zone".
    private static string Normalize(string columnType)
    {
        var text = new StringBuilder(columnType.Length);
        var depth = 0;
        var pendingSpace = false;
        foreach (var c in columnType)
        {
            if (c == '(') { depth++; continue; }
            if (c == ')') { depth = Math.Max(0, depth - 1); continue; }
            if (depth > 0) continue;

            if (char.IsWhiteSpace(c)) { pendingSpace = text.Length > 0; continue; }
            if (pendingSpace) text.Append(' ');
            pendingSpace = false;
            text.Append(char.ToLowerInvariant(c));
        }

        return text.ToString();
    }
}
