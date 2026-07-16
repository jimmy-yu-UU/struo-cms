// src/Struo.Application/Query/Write/ItemDeserializer.cs
using System.Text.Json;
using Struo.Application.Metadata;
using Struo.Domain.Metadata.Enums;
using Struo.Domain.Metadata.Models;
using Struo.Domain.Query;

namespace Struo.Application.Query;

/// <summary>
/// Binds a request body into a parent entity and runs the full write-side field validation /
/// normalization pipeline (RichText sanitize, Required, MaxLength, and the per-interface validator
/// phases). Extracted verbatim from <see cref="ItemService"/> (ARC-1); the phase ORDER is observable
/// (exception precedence) and preserved exactly.
/// </summary>
public sealed class ItemDeserializer(IEntityRegistry registry, IM2MDescriptorSource m2mSource, RichTextCleaner richText)
{
    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

    private readonly FieldValidatorRegistry validatorRegistry = new();

    public object Deserialize(string collection, JsonElement body, CollectionMetadata meta)
    {
        var d = registry.Get(collection) ?? throw new CollectionNotFoundException(collection);

        // Strip M2M relation keys (e.g. "tags": [1,2]) from the body before deserializing into
        // the entity type — those keys hold id arrays, not nested objects, so JSON deserialization
        // would fail trying to convert a long into the target entity type.
        var stripNames = m2mSource.M2MDescriptors(collection)
            .Select(r => r.RelationName)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        // The translation sidecar payload ("translations": { locale: {...} }) is not a parent
        // property — strip it so deserialization into the parent entity doesn't choke on it.
        if (meta.Translation is not null) stripNames.Add("translations");

        // Json fields carry an arbitrary JSON object/array/scalar; binding that into their string
        // property would make System.Text.Json throw. Strip them here and set the raw text below.
        foreach (var jsonField in meta.Fields.Where(f => f.Interface == FieldInterface.Json))
            stripNames.Add(jsonField.Name);

        object entity;
        if (stripNames.Count > 0)
        {
            // Parse into a using-scoped document so the ArrayPool buffer is returned promptly.
            using var stripped = JsonDocument.Parse(JsonBodyUtil.StripKeys(body, stripNames));
            try
            {
                entity = stripped.RootElement.Deserialize(d.EntityType, JsonOpts)
                         ?? throw new QueryException("Request body could not be parsed.");
            }
            catch (JsonException)
            {
                // A field's JSON value is the wrong shape for its bound property (e.g. a KeyValue
                // entry given a number/object instead of a string) — a client error, not a 500.
                throw new QueryException("Request body could not be parsed.");
            }
        }
        else
        {
            try
            {
                entity = body.Deserialize(d.EntityType, JsonOpts)
                         ?? throw new QueryException("Request body could not be parsed.");
            }
            catch (JsonException)
            {
                throw new QueryException("Request body could not be parsed.");
            }
        }

        // Set each Json field's string property from the original body's raw text (stripped above).
        // Present + non-null => store the raw JSON text; absent or explicit JSON null => leave null.
        foreach (var field in meta.Fields.Where(f => f.Interface == FieldInterface.Json && !f.Translatable))
        {
            if (!d.FieldToProperty.TryGetValue(field.Name, out var prop)) continue;
            var pi = d.Properties.GetValueOrDefault(prop);
            if (pi is not { CanWrite: true } || pi.PropertyType != typeof(string)) continue;
            if (body.ValueKind == JsonValueKind.Object
                && body.TryGetProperty(field.Name, out var el)
                && el.ValueKind != JsonValueKind.Null)
            {
                pi.SetValue(entity, el.GetRawText());
            }
        }

        // strip system/read-only fields (audit AOP / identity own them); only nullable props can be nulled
        foreach (var field in meta.Fields.Where(f => f.IsSystem || f.ReadOnly))
        {
            if (!d.FieldToProperty.TryGetValue(field.Name, out var prop)) continue;
            var pi = d.Properties.GetValueOrDefault(prop);
            if (pi is not { CanWrite: true }) continue;
            var canBeNull = !pi.PropertyType.IsValueType || Nullable.GetUnderlyingType(pi.PropertyType) is not null;
            if (canBeNull) pi.SetValue(entity, null);
        }

        // Sanitize non-translatable RichText field values on the entity before required validation,
        // so stored HTML is XSS-clean and a blank editor document is treated as missing.
        foreach (var field in meta.Fields.Where(f => f.Interface == FieldInterface.RichText && !f.Translatable))
        {
            if (!d.FieldToProperty.TryGetValue(field.Name, out var prop)) continue;
            var pi = d.Properties.GetValueOrDefault(prop);
            if (pi is not { CanWrite: true } || pi.PropertyType != typeof(string)) continue;
            if (pi.GetValue(entity) is string raw)
                pi.SetValue(entity, richText.Sanitize(raw));
        }

        // required validation — skip translatable fields (they live on the sidecar entity and
        // are validated per-locale in SyncTranslationsAsync, not on the parent).
        foreach (var field in meta.Fields.Where(f => f.Required && !f.Translatable))
        {
            var pi = d.FieldToProperty.TryGetValue(field.Name, out var prop) ? d.Properties.GetValueOrDefault(prop) : null;
            var value = pi?.GetValue(entity);
            FieldValueRules.RequireParent(field.Name, value);
        }

        // Max length — CMS-layer limit (7g.5); the DB column width is SqlSugar's separate concern.
        // Runs after RichText sanitization so the stored value is what gets measured.
        foreach (var field in meta.Fields.Where(f => f.MaxLength is > 0 && !f.Translatable))
        {
            var pi = d.FieldToProperty.TryGetValue(field.Name, out var prop) ? d.Properties.GetValueOrDefault(prop) : null;
            if (pi?.GetValue(entity) is string value)
                FieldValueRules.CheckMaxLengthParent(field.Name, field.MaxLength!.Value, value);
        }

        // Per-interface validator phases (multi-value → KeyValue → Files → Repeater). Each phase
        // iterates meta.Fields in declaration order, skips Translatable, resolves the property exactly
        // as the field overlays above (FieldToProperty → OrdinalIgnoreCase Properties map; skip if not
        // writable), then dispatches to the phase's validator. The phase order is observable
        // (exception precedence when several fields are invalid) and preserved by FieldValidatorRegistry.
        foreach (var (interfaces, validators) in validatorRegistry.Phases)
        {
            foreach (var field in meta.Fields.Where(f => interfaces.Contains(f.Interface) && !f.Translatable))
            {
                if (!d.FieldToProperty.TryGetValue(field.Name, out var prop)) continue;
                var pi = d.Properties.GetValueOrDefault(prop);
                if (pi is not { CanWrite: true }) continue;
                validators[field.Interface].ValidateAndNormalize(field, pi, entity);
            }
        }

        return entity;
    }
}
