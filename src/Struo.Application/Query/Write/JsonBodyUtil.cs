// src/Struo.Application/Query/Write/JsonBodyUtil.cs
using System.Globalization;
using System.Text.Json;
using Struo.Domain.Query;

namespace Struo.Application.Query;

/// <summary>
/// Pure JSON helpers shared by the write path: strip top-level keys, coerce a JSON value to a CLR
/// primitive for translation storage, and read the optimistic-concurrency token from a body.
/// </summary>
internal static class JsonBodyUtil
{
    /// <summary>
    /// The array a body carries under <paramref name="relationName"/>, read exactly as the relation sync reads
    /// it: the exact name, the last occurrence when it repeats, and only when the value is an array. The guard
    /// that decides who may assign a role and the sync that assigns it both call this, so they cannot disagree.
    /// </summary>
    internal static bool TryGetRelationArray(JsonElement body, string relationName, out JsonElement array)
    {
        array = default;
        return body.ValueKind == JsonValueKind.Object
               && body.TryGetProperty(relationName, out array)
               && array.ValueKind == JsonValueKind.Array;
    }

    /// <summary>
    /// Refuses (400) a body that names a top-level property more than once. Two readers of a repeated name
    /// can pick different occurrences, so the item write path accepts no such body. Names that differ only
    /// in casing are different properties.
    /// </summary>
    internal static void RequireUniqueProperties(JsonElement body)
    {
        if (body.ValueKind != JsonValueKind.Object) return;
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var repeated = body.EnumerateObject().Select(p => p.Name).FirstOrDefault(name => !seen.Add(name));
        if (repeated is not null)
            throw new QueryException($"The request body names the property '{repeated}' more than once.");
    }

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
            foreach (var prop in source.EnumerateObject().Where(p => !keysToRemove.Contains(p.Name)))
                prop.WriteTo(writer);
            writer.WriteEndObject();
        }
        return ms.ToArray();
    }

    /// <summary>
    /// Serialises <paramref name="source"/> with its top-level <c>version</c> key replaced by
    /// <paramref name="version"/>, or removed when <paramref name="version"/> is null.
    /// </summary>
    internal static byte[] WithVersion(JsonElement source, long? version)
    {
        using var ms = new System.IO.MemoryStream();
        using (var writer = new System.Text.Json.Utf8JsonWriter(ms))
        {
            writer.WriteStartObject();
            foreach (var prop in source.EnumerateObject()
                         .Where(p => !string.Equals(p.Name, "version", StringComparison.OrdinalIgnoreCase)))
                prop.WriteTo(writer);
            if (version is { } v) writer.WriteNumber("version", v);
            writer.WriteEndObject();
        }
        return ms.ToArray();
    }

    /// <summary>
    /// Serialises <paramref name="source"/> with <c>"&lt;locale&gt;": null</c> appended to its top-level
    /// <c>translations</c> object for each of <paramref name="locales"/> that object does not carry
    /// (case-insensitive). A body without a <c>translations</c> object is copied unchanged.
    /// </summary>
    internal static byte[] WithNullLocales(JsonElement source, IEnumerable<string> locales)
    {
        using var ms = new System.IO.MemoryStream();
        using (var writer = new System.Text.Json.Utf8JsonWriter(ms))
        {
            writer.WriteStartObject();
            foreach (var prop in source.EnumerateObject())
            {
                if (!string.Equals(prop.Name, "translations", StringComparison.Ordinal)
                    || prop.Value.ValueKind != JsonValueKind.Object)
                {
                    prop.WriteTo(writer);
                    continue;
                }
                writer.WritePropertyName(prop.Name);
                writer.WriteStartObject();
                var present = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var locale in prop.Value.EnumerateObject())
                {
                    present.Add(locale.Name);
                    locale.WriteTo(writer);
                }
                foreach (var missing in locales.Where(l => !present.Contains(l)))
                    writer.WriteNull(missing);
                writer.WriteEndObject();
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

    // Reads the optimistic-concurrency token the client echoed back. The key matches `version` ignoring
    // case, like field matching and WithVersion, and may appear once. Accepts a JSON integer or a
    // numeric string; returns null when the property is absent or JSON null ("no version supplied")
    // and throws InvalidVersionException for a repeated key or any other value.
    internal static long? ReadVersion(JsonElement body)
    {
        if (body.ValueKind != JsonValueKind.Object) return null;
        JsonElement? found = null;
        foreach (var prop in body.EnumerateObject())
        {
            if (!string.Equals(prop.Name, "version", StringComparison.OrdinalIgnoreCase)) continue;
            if (found is not null) throw new InvalidVersionException("version must be supplied at most once.");
            found = prop.Value;
        }
        if (found is not { } v) return null;
        return v.ValueKind switch
        {
            JsonValueKind.Null => null,
            JsonValueKind.Number when v.TryGetInt64(out var n) => n,
            JsonValueKind.String when long.TryParse(v.GetString(), NumberStyles.AllowLeadingSign,
                CultureInfo.InvariantCulture, out var n) => n,
            _ => throw new InvalidVersionException("version must be an integer.")
        };
    }
}
