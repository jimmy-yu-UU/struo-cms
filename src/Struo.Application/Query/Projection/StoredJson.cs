// src/Struo.Application/Query/Projection/StoredJson.cs
using System.Text.Json;
using Struo.Domain.Metadata.Enums;
using Struo.Domain.Metadata.Models;

namespace Struo.Application.Query;

/// <summary>
/// Reads the raw JSON text a <c>Json</c> field stores (root or translatable) back as the value the API
/// emits. One definition, shared by the item projection, the translation overlay and the revision snapshot.
/// </summary>
internal static class StoredJson
{
    /// <summary>The names of the translatable <c>Json</c> fields of a collection, case-insensitive.</summary>
    public static HashSet<string> TranslatableJsonFields(CollectionMetadata meta) =>
        meta.Fields
            .Where(f => f.Translatable && f.Interface == FieldInterface.Json)
            .Select(f => f.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Parses stored JSON text to a fresh (non-disposed) <see cref="JsonElement"/> so the API emits
    /// structured JSON, not a quoted string. A value that is not a string, and text that is not valid JSON,
    /// come back unchanged: the write path only stores valid JSON, so non-JSON text is an out-of-band row,
    /// and returning it as the raw string keeps the rest of the page loading instead of a 500.
    /// </summary>
    public static object? Read(object? stored)
    {
        if (stored is not string raw) return stored;
        try
        {
            return JsonSerializer.Deserialize<JsonElement>(raw);
        }
        catch (JsonException)
        {
            return stored;
        }
    }
}
