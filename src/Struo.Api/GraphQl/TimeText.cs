// src/Struo.Api/GraphQl/TimeText.cs
using System.Globalization;

namespace Struo.Api.GraphQl;

/// <summary>
/// The text of a <c>Time</c> field. The schema types it <c>String</c>, but the property holds a
/// <see cref="DateTime"/>, and the String scalar prints a DateTime of unspecified kind with the host's
/// offset. A DateTime in this application is a UTC instant, so the text is that instant in UTC ending in
/// <c>Z</c>, with fractional seconds only when there are some: the form the GraphQL DateTime scalar prints.
/// </summary>
internal static class TimeText
{
    internal static object? Of(object? value) => value is DateTime dateTime ? Format(dateTime) : value;

    internal static string Format(DateTime value)
    {
        var utc = value.Kind == DateTimeKind.Local ? value.ToUniversalTime() : value;
        return utc.ToString("yyyy-MM-dd'T'HH':'mm':'ss.FFFFFFF'Z'", CultureInfo.InvariantCulture);
    }
}
