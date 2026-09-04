// src/Struo.Application/Query/RevisionSnapshotRedactor.cs
using System.Text;
using System.Text.Json;
using Struo.Application.Metadata;
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
    /// <summary>Equivalent to <see cref="RedactHidden(string,CollectionMetadata,IReadOnlyList{M2MDescriptor})"/>
    /// with no M2M descriptors, i.e. no relation carries junction payload to redact.</summary>
    public static string RedactHidden(string snapshotJson, CollectionMetadata meta) =>
        RedactHidden(snapshotJson, meta, []);

    /// <summary>
    /// Returns a NEW JSON string equal to <paramref name="snapshotJson"/> except: (1) any top-level key
    /// whose field metadata has <c>Hidden == true</c> is omitted; (2) inside every
    /// <c>translations.{locale}</c> object, any key belonging to a field with <c>Hidden == true</c> AND
    /// <c>Translatable == true</c> is omitted; and (3) for every relation in <paramref name="m2m"/> that
    /// carries junction payload with at least one <c>Hidden</c> field, each object element of that
    /// relation's array (an <c>{ id, ...payload }</c> element written by <see cref="RevisionSnapshotBuilder"/>)
    /// has its hidden payload keys omitted — a bare-id array element is left untouched. Every other
    /// value — nested objects/arrays, numbers, booleans, nulls — is copied through unchanged. Never
    /// mutates <paramref name="snapshotJson"/> or any parsed <see cref="JsonDocument"/> (each is scoped
    /// with <c>using</c>).
    /// </summary>
    public static string RedactHidden(string snapshotJson, CollectionMetadata meta, IReadOnlyList<M2MDescriptor> m2m)
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
        var hiddenPayloadByRelation = m2m
            .Where(d => d.HasPayload)
            .Select(d => (d.RelationName, Hidden: (IReadOnlySet<string>)new HashSet<string>(
                d.JunctionPayload!.Where(f => f.Hidden).Select(f => f.Name), StringComparer.OrdinalIgnoreCase)))
            .Where(t => t.Hidden.Count > 0)
            .ToDictionary(t => t.RelationName, t => t.Hidden, StringComparer.OrdinalIgnoreCase);

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

                if (prop.Value.ValueKind == JsonValueKind.Array
                    && hiddenPayloadByRelation.TryGetValue(prop.Name, out var hiddenPayload))
                {
                    writer.WritePropertyName(prop.Name);
                    WriteRedactedRelation(writer, prop.Value, hiddenPayload);
                    continue;
                }

                prop.WriteTo(writer);
            }
            writer.WriteEndObject();
        }
        return Encoding.UTF8.GetString(ms.ToArray());
    }

    /// <summary>Rewrites one M2M relation array, dropping <paramref name="hiddenPayload"/> keys from each
    /// object element (a payload-carrying <c>{ id, ...payload }</c> element); a bare-id (string) element
    /// is copied through unchanged, since a bare id carries no payload to redact.</summary>
    private static void WriteRedactedRelation(
        Utf8JsonWriter writer, JsonElement array, IReadOnlySet<string> hiddenPayload)
    {
        writer.WriteStartArray();
        foreach (var element in array.EnumerateArray())
        {
            if (element.ValueKind != JsonValueKind.Object)
            {
                element.WriteTo(writer);
                continue;
            }

            writer.WriteStartObject();
            foreach (var field in element.EnumerateObject())
            {
                if (hiddenPayload.Contains(field.Name)) continue;
                field.WriteTo(writer);
            }
            writer.WriteEndObject();
        }
        writer.WriteEndArray();
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
