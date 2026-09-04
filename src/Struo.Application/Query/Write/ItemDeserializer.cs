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
/// phases). Extracted verbatim from <see cref="ItemService"/>; the phase ORDER is observable
/// (exception precedence) and preserved exactly.
/// </summary>
public sealed class ItemDeserializer(IEntityRegistry registry, IM2MDescriptorSource m2mSource, RichTextCleaner richText)
{
    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

    private readonly FieldValidatorRegistry validatorRegistry = new();

    public object Deserialize(string collection, JsonElement body, CollectionMetadata meta) =>
        DeserializeCore(collection, body, meta, enforceRequired: true, onlyFields: null);

    /// <summary>
    /// Binds a partial object — used for M2M junction payload — through the same allowlist, JSON-field,
    /// RichText, MaxLength and per-interface validation as <see cref="Deserialize"/>, but only for the
    /// camelCase fields in <paramref name="onlyFields"/> that are present in <paramref name="body"/>, and
    /// without Required checks (absent payload fields keep their stored value). Returns CLR property name → value.
    /// </summary>
    public IReadOnlyDictionary<string, object?> DeserializePartial(
        string collection, JsonElement body, CollectionMetadata meta, IReadOnlySet<string> onlyFields)
    {
        var d = registry.Get(collection) ?? throw new CollectionNotFoundException(collection);
        var entity = DeserializeCore(collection, body, meta, enforceRequired: false, onlyFields);
        var result = new Dictionary<string, object?>(StringComparer.Ordinal);
        if (body.ValueKind != JsonValueKind.Object) return result;

        // JsonElement.TryGetProperty is case-sensitive; onlyFields (and DeserializeCore's allowedKeys)
        // compare OrdinalIgnoreCase, so match "is this field present in body" the same way.
        var presentNames = body.EnumerateObject().Select(p => p.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var name in onlyFields)
        {
            if (!presentNames.Contains(name)) continue;
            if (!d.FieldToProperty.TryGetValue(name, out var prop)) continue;
            var pi = d.Properties.GetValueOrDefault(prop);
            if (pi is not { CanWrite: true }) continue;
            result[prop] = pi.GetValue(entity);
        }
        return result;
    }

    private object DeserializeCore(
        string collection, JsonElement body, CollectionMetadata meta, bool enforceRequired, IReadOnlySet<string>? onlyFields)
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

        // Allowlist everything else: only declared, writable [CmsField]s and declared ManyToOne
        // foreign keys may bind directly into the CLR entity. Without this, deserializing straight
        // into the whole entity type lets a client set anything the CLR type exposes but metadata
        // never declared — the primary key, the optimistic-concurrency Version, the soft-delete
        // markers, or a fork's own undeclared property — none of which carry [CmsField] and so none
        // of which are covered by the IsSystem/ReadOnly strip loop below. This mirrors the allowlist
        // ItemService.UpdateCoreAsync already applies on update (fields + M2O FKs actually present in
        // the body); unknown keys are silently ignored here too, not rejected with a 400.
        var allowedKeys = meta.Fields.Where(f => !f.IsSystem && !f.ReadOnly)
            .Select(f => f.Name)
            .Concat(meta.Relations
                .Where(r => r.Kind == RelationKind.ManyToOne && r.ForeignKey is not null)
                .Select(r => r.ForeignKey!))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (onlyFields is not null) allowedKeys.IntersectWith(onlyFields);
        if (body.ValueKind == JsonValueKind.Object)
        {
            foreach (var name in body.EnumerateObject().Select(p => p.Name).Where(n => !allowedKeys.Contains(n)))
                stripNames.Add(name);
        }

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

        // strip system/read-only fields (audit columns and identity are owned by AOP); only nullable props can be nulled
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
        // are validated per-locale in SyncTranslationsAsync, not on the parent). Skipped entirely for a
        // partial bind (enforceRequired: false) — a junction payload object only sets the fields it
        // names, so a required payload field absent from this particular element is not an error;
        // absent payload fields keep their stored value.
        if (enforceRequired)
        {
            foreach (var field in meta.Fields.Where(f => f.Required && !f.Translatable))
            {
                var pi = d.FieldToProperty.TryGetValue(field.Name, out var prop) ? d.Properties.GetValueOrDefault(prop) : null;
                var value = pi?.GetValue(entity);
                FieldValueRules.RequireParent(field.Name, value);
            }
        }

        // Max length — a CMS-layer limit; the DB column width is SqlSugar's separate concern.
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
