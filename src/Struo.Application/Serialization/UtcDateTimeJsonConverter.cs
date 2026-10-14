using System.Text.Json;
using System.Text.Json.Serialization;

namespace Struo.Application.Serialization;

/// <summary>
/// Reads and writes every <see cref="DateTime"/> as a UTC instant. A string with <c>Z</c> or an
/// offset is read as that instant, a string without a zone is read as UTC, and the written form
/// always ends in <c>Z</c>. Also applies to <c>DateTime?</c>.
/// </summary>
public sealed class UtcDateTimeJsonConverter : JsonConverter<DateTime>
{
    public override DateTime Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (!reader.TryGetDateTime(out var value))
            throw new JsonException("The JSON value is not a valid date-time.");

        // Local means the text carried a non-Z offset. Re-read it as an offset so the instant never
        // passes through the host time zone.
        if (value.Kind == DateTimeKind.Local)
        {
            if (!reader.TryGetDateTimeOffset(out var offset))
                throw new JsonException("The JSON value is not a valid date-time.");
            return offset.UtcDateTime;
        }

        return DateTime.SpecifyKind(value, DateTimeKind.Utc);
    }

    public override void Write(Utf8JsonWriter writer, DateTime value, JsonSerializerOptions options)
    {
        var utc = value.Kind switch
        {
            DateTimeKind.Utc => value,
            DateTimeKind.Local => value.ToUniversalTime(),
            _ => DateTime.SpecifyKind(value, DateTimeKind.Utc),
        };
        writer.WriteStringValue(utc);
    }
}
