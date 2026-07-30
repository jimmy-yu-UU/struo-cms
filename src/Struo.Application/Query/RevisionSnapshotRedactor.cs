// src/Struo.Application/Query/RevisionSnapshotRedactor.cs
using System.Text;
using System.Text.Json;
using Struo.Domain.Metadata.Models;

namespace Struo.Application.Query;

/// <summary>
/// Produces the externally-safe view of a revision snapshot. <see cref="RevisionSnapshotBuilder"/>
/// deliberately captures every field of an item — including ones marked
/// <see cref="Struo.Domain.Metadata.Models.FieldMetadata.Hidden"/> — because a revert must be able to
/// restore the full item state (see <c>ItemService.RevertAsync</c>, which reads the raw, unredacted
/// snapshot straight from the revision store). This type strips hidden-field values out of a *copy* of
/// that snapshot before it is handed to any external caller (REST <c>GET
/// /api/items/{collection}/{id}/revisions/{n}</c> or GraphQL <c>xRevision</c>), so a hidden/credential-class
/// field never leaves the process except through the revert path.
/// </summary>
public static class RevisionSnapshotRedactor
{
    /// <summary>
    /// Returns a NEW JSON string equal to <paramref name="snapshotJson"/> except: (1) any top-level key
    /// whose field metadata has <c>Hidden == true</c> is omitted, and (2) inside every
    /// <c>translations.{locale}</c> object, any key belonging to a field with <c>Hidden == true</c> AND
    /// <c>Translatable == true</c> is omitted. Every other value — nested objects/arrays, numbers,
    /// booleans, nulls — is copied through unchanged. Never mutates <paramref name="snapshotJson"/> or
    /// any parsed <see cref="JsonDocument"/> (each is scoped with <c>using</c>).
    /// </summary>
    public static string RedactHidden(string snapshotJson, CollectionMetadata meta)
    {
        using var doc = JsonDocument.Parse(snapshotJson);
        var root = doc.RootElement;
        if (root.ValueKind != JsonValueKind.Object) return snapshotJson;

        var hiddenTopLevel = new HashSet<string>(
            meta.Fields.Where(f => f.Hidden).Select(f => f.Name),
            StringComparer.OrdinalIgnoreCase);
        var hiddenTranslatable = new HashSet<string>(
            meta.Fields.Where(f => f.Hidden && f.Translatable).Select(f => f.Name),
            StringComparer.OrdinalIgnoreCase);

        using var ms = new MemoryStream();
        using (var writer = new Utf8JsonWriter(ms))
        {
            writer.WriteStartObject();
            foreach (var prop in root.EnumerateObject())
            {
                if (hiddenTopLevel.Contains(prop.Name)) continue;

                if (prop.NameEquals("translations") && prop.Value.ValueKind == JsonValueKind.Object)
                {
                    writer.WritePropertyName(prop.Name);
                    WriteRedactedTranslations(writer, prop.Value, hiddenTranslatable);
                    continue;
                }

                prop.WriteTo(writer);
            }
            writer.WriteEndObject();
        }
        return Encoding.UTF8.GetString(ms.ToArray());
    }

    private static void WriteRedactedTranslations(
        Utf8JsonWriter writer, JsonElement translations, IReadOnlySet<string> hiddenTranslatable)
    {
        writer.WriteStartObject();
        foreach (var locale in translations.EnumerateObject())
        {
            writer.WritePropertyName(locale.Name);
            if (hiddenTranslatable.Count == 0 || locale.Value.ValueKind != JsonValueKind.Object)
            {
                locale.Value.WriteTo(writer);
                continue;
            }

            writer.WriteStartObject();
            foreach (var field in locale.Value.EnumerateObject())
            {
                if (hiddenTranslatable.Contains(field.Name)) continue;
                field.WriteTo(writer);
            }
            writer.WriteEndObject();
        }
        writer.WriteEndObject();
    }
}
