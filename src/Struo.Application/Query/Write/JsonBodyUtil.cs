// src/Struo.Application/Query/Write/JsonBodyUtil.cs
using System.Text.Json;

namespace Struo.Application.Query;

/// <summary>
/// Pure JSON helpers shared by the write path: strip top-level keys, coerce a JSON value to a CLR
/// primitive for translation storage, and read the optimistic-concurrency token from a body.
/// </summary>
internal static class JsonBodyUtil
{
    /// <summary>
    /// Serialises <paramref name="source"/> as UTF-8 JSON with any top-level key in
    /// <paramref name="keysToRemove"/> omitted. Returns the raw bytes so the caller can
    /// parse them into a <c>using</c>-scoped <see cref="JsonDocument"/> and avoid a pool leak.
    /// </summary>
    internal static byte[] StripKeys(JsonElement source, IReadOnlySet<string> keysToRemove)
    {
        using var ms = new System.IO.MemoryStream();
        using (var writer = new System.Text.Json.Utf8JsonWriter(ms))
        {
            writer.WriteStartObject();
            foreach (var prop in source.EnumerateObject())
            {
                if (!keysToRemove.Contains(prop.Name))
                    prop.WriteTo(writer);
            }
            writer.WriteEndObject();
        }
        return ms.ToArray();
    }

    /// <summary>Converts a JSON value to a CLR primitive for translation field storage.</summary>
    internal static object? JsonValue(JsonElement e) => e.ValueKind switch
    {
        JsonValueKind.String => e.GetString(),
        JsonValueKind.Number => e.TryGetInt64(out var l) ? l : e.GetDouble(),
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        JsonValueKind.Null => null,
        _ => e.GetRawText()
    };

    // Reads the optimistic-concurrency token the client echoed back, if any. Accepts a JSON number or
    // a numeric string; returns null when absent or unparseable (treated as "no version supplied").
    internal static long? TryReadVersion(JsonElement body)
    {
        if (body.ValueKind != JsonValueKind.Object) return null;
        if (!body.TryGetProperty("version", out var v)) return null;
        return v.ValueKind switch
        {
            JsonValueKind.Number when v.TryGetInt64(out var n) => n,
            JsonValueKind.String when long.TryParse(v.GetString(), out var n) => n,
            _ => null
        };
    }
}
