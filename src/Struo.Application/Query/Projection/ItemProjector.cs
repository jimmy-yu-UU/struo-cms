// src/Struo.Application/Query/Projection/ItemProjector.cs
using System.Text.Json;
using Struo.Application.Metadata;
using Struo.Application.Security;
using Struo.Domain.Metadata.Enums;
using Struo.Domain.Metadata.Models;
using Struo.Domain.Query;

namespace Struo.Application.Query;

/// <summary>
/// Projects a persisted entity into the outbound field/value dictionary the API emits, honoring
/// field selection, per-field read permissions, hidden/system flags, and the always-present
/// <c>id</c>/<c>version</c> keys. Extracted verbatim from <see cref="ItemService"/>; the
/// concrete return type stays <see cref="Dictionary{TKey,TValue}"/> because callers (<c>GetAsync</c>,
/// the translation overlay, deep-expansion) downcast the rows.
/// </summary>
public sealed class ItemProjector(IEntityRegistry registry, IPermissionService permissions, IMetadataProvider metadata)
{
    /// <summary>Reuses the metadata projection for an arbitrary target collection.</summary>
    public IReadOnlyDictionary<string, object?> ProjectFor(
        string collectionName, object entity, IReadOnlyList<string>? fields) =>
        Project(entity, Meta(collectionName), fields);

    public IReadOnlyDictionary<string, object?> Project(object entity, CollectionMetadata meta, IReadOnlyList<string>? fields)
    {
        var d = registry.Get(meta.Name)!;
        var dict = new Dictionary<string, object?>();

        const string idKey = "id";
        // d.IdProperty is an exact CLR property name; d.Properties is keyed OrdinalIgnoreCase, so this
        // resolves the same PropertyInfo the old case-sensitive GetProperty(d.IdProperty) returned.
        dict[idKey] = d.Properties.GetValueOrDefault(d.IdProperty)?.GetValue(entity);

        // Always expose the concurrency token (like id, independent of field selection) so the client
        // can echo it back on update for optimistic-locking.
        if (entity is Struo.Domain.Auditing.AuditableEntity versioned)
            dict["version"] = versioned.Version;

        var wanted = fields is { Count: > 0 } ? fields.ToHashSet(StringComparer.OrdinalIgnoreCase) : null;
        var readable = permissions.ReadableFields(meta.Name, meta.Fields.Select(f => f.Name))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var field in meta.Fields)
        {
            if (field.Hidden) continue;
            if (field.Name == idKey) continue;
            if (wanted is not null && !wanted.Contains(field.Name)) continue;
            if (!readable.Contains(field.Name)) continue;
            if (!d.FieldToProperty.TryGetValue(field.Name, out var prop)) continue;
            // `prop` is an exact CLR property name (a FieldToProperty value); the OrdinalIgnoreCase
            // Properties map returns the same PropertyInfo the old case-sensitive GetProperty(prop) did.
            var value = d.Properties.GetValueOrDefault(prop)?.GetValue(entity);
            // Json fields store raw JSON text; parse to a fresh (non-disposed) JsonElement so the API
            // emits structured JSON, not a quoted string. Null stays null.
            if (field.Interface == FieldInterface.Json && value is string rawJson)
            {
                try
                {
                    value = JsonSerializer.Deserialize<JsonElement>(rawJson);
                }
                catch (JsonException)
                {
                    // Defensive: the write path only ever stores valid JSON, so this is unreachable
                    // via the API. Guards against out-of-band/legacy rows holding non-JSON text —
                    // leave the raw string so the rest of the page still loads instead of a 500.
                }
            }
            dict[field.Name] = value;
        }
        return dict;
    }

    private CollectionMetadata Meta(string collection) =>
        metadata.GetCollection(collection) ?? throw new CollectionNotFoundException(collection);
}
