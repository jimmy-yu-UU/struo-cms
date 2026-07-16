// src/Struo.Application/Query/Write/ItemWriteSideSync.cs
using System.Text.Json;
using Struo.Application.Localization;
using Struo.Application.Metadata;
using Struo.Domain.Metadata.Models;
using Struo.Domain.Query;

namespace Struo.Application.Query;

/// <summary>
/// Write-side relation/translation synchronization, extracted verbatim from <see cref="ItemService"/>
/// (ARC-1). Both methods run INSIDE the caller's write transaction (see
/// <see cref="ItemService.CreateAsync"/> / <c>UpdateCoreAsync</c>), so a failure here rolls the whole
/// write back. Validation phase order and exception message strings are observable and preserved exactly.
/// </summary>
public sealed class ItemWriteSideSync(
    IItemRepository repository,
    IM2MDescriptorSource m2mSource,
    ILanguageProvider languages,
    RichTextCleaner richText)
{
    /// <summary>
    /// When <paramref name="meta"/> declares a translation sidecar and the request body carries a
    /// <c>translations</c> object, validates each locale (enabled), each field key (⊆ translatable
    /// fields), and required fields, then delegates to
    /// <see cref="IItemRepository.SyncTranslationsAsync"/>.
    /// <para>
    /// On create (<paramref name="isCreate"/> = true) the default-locale translation is mandatory
    /// (Spec §10): an absent <c>translations</c> key, a non-object value, an empty object, or an
    /// object lacking the default locale all throw <see cref="QueryException"/> (→ 400). On update
    /// an absent/non-object <c>translations</c> payload is a no-op (partial updates supported) and
    /// the default locale is NOT forced.
    /// </para>
    /// </summary>
    public async Task SyncTranslationsAsync(
        CollectionMetadata meta, JsonElement body, object parentId, bool isCreate, CancellationToken ct)
    {
        var tm = meta.Translation;
        if (tm is null) return;

        var hasTranslations = body.TryGetProperty("translations", out var trElem)
                              && trElem.ValueKind == JsonValueKind.Object;

        // On create the default-locale translation is required. Reject an absent/non-object
        // `translations` payload HERE, before returning early — otherwise a body with no
        // `translations` key would silently create a row with zero translation rows (Spec §10).
        if (!hasTranslations)
        {
            if (isCreate)
                throw new QueryException(
                    $"A translation for the default locale '{languages.DefaultCode()}' is required.");
            return; // update: absent/non-object translations = no-op partial update
        }

        var allowed = tm.Fields.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var requiredFields = meta.Fields
            .Where(f => f.Translatable && f.Required)
            .Select(f => f.Name)
            .ToList();
        var maxLengths = meta.Fields
            .Where(f => f.MaxLength is > 0)
            .ToDictionary(f => f.Name, f => f.MaxLength!.Value, StringComparer.OrdinalIgnoreCase);

        var perLocale = new Dictionary<string, IReadOnlyDictionary<string, object?>>(StringComparer.OrdinalIgnoreCase);

        foreach (var localeProp in trElem.EnumerateObject())
        {
            var locale = localeProp.Name;
            if (!languages.IsEnabled(locale))
                throw new QueryException($"Unknown or disabled locale '{locale}'.");
            if (localeProp.Value.ValueKind != JsonValueKind.Object)
                throw new QueryException($"Translation for locale '{locale}' must be an object.");

            var fieldValues = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
            foreach (var field in localeProp.Value.EnumerateObject())
            {
                if (!allowed.Contains(field.Name))
                    throw new QueryException(
                        $"Field '{field.Name}' is not a translatable field of '{meta.Name}'.");
                var value = JsonBodyUtil.JsonValue(field.Value);
                if (value is string s && RichTextCleaner.IsRichTextField(meta, field.Name))
                    value = richText.Sanitize(s);
                fieldValues[field.Name] = value;
            }

            // Required: every required translatable field must be present and non-empty.
            foreach (var rf in requiredFields)
            {
                var has = fieldValues.TryGetValue(rf, out var v);
                FieldValueRules.RequireTranslation(rf, locale, has, v);
            }

            // Max length — CMS-layer limit (7g.5), measured after RichText sanitization.
            foreach (var (name, value) in fieldValues)
            {
                if (value is string sv && maxLengths.TryGetValue(name, out var max))
                    FieldValueRules.CheckMaxLengthTranslation(name, max, locale, sv);
            }

            perLocale[locale] = fieldValues;
        }

        // On create, the default locale's translation must be supplied.
        if (isCreate)
        {
            var def = languages.DefaultCode();
            if (!perLocale.Keys.Any(k => string.Equals(k, def, StringComparison.OrdinalIgnoreCase)))
                throw new QueryException($"A translation for the default locale '{def}' is required.");
        }

        if (perLocale.Count == 0) return;

        await repository.SyncTranslationsAsync(
            tm.TranslationEntityType,
            tm.ForeignKeyProperty,
            tm.LocaleProperty,
            tm.Fields,
            parentId,
            perLocale,
            ct);
    }

    /// <summary>
    /// For each M2M relation declared on <paramref name="collection"/>, reads the target-id array
    /// from <paramref name="body"/> under the relation's camelCase name (e.g. <c>"tags"</c>).
    /// If the key is present, validates every id exists in the target collection, then delegates
    /// to <see cref="IItemRepository.SyncManyToManyAsync"/> to replace the junction rows.
    /// Absent keys are silently skipped (partial updates are supported).
    /// </summary>
    public async Task SyncM2MAsync(string collection, JsonElement body, object parentId, bool includeDeleted, CancellationToken ct)
    {
        var descs = m2mSource.M2MDescriptors(collection);
        if (descs.Count == 0) return;

        foreach (var desc in descs)
        {
            // Only sync when the relation key is present in the request body.
            if (!body.TryGetProperty(desc.RelationName, out var idsElem)) continue;
            if (idsElem.ValueKind != System.Text.Json.JsonValueKind.Array) continue;

            // DB-9: de-duplicate the incoming ids up front — `tags:[t1,t1]` is semantically `tags:[t1]`
            // (a junction is a set). Distinct() returns a NEW list (no in-place mutation) and the typed
            // boxed values (long / string) compare correctly under the default equality comparer. Without
            // this, a repeated id inflated targetIds.Count so the count-based existence check below
            // spuriously failed ("do not exist"), and the junction sync would attempt duplicate rows.
            var targetIds = idsElem.EnumerateArray()
                .Select(e => e.ValueKind == JsonValueKind.Number
                    ? (object)e.GetInt64()
                    : (object)(e.GetString() ?? string.Empty))
                .Distinct()
                .ToList();

            // Validate all target ids exist. DB-9: a REVERT (includeDeleted) may legitimately reference a
            // target that has since been trashed — the snapshot was captured while it was still live — so
            // it validates against the soft-delete-bypassing query; a normal write keeps the strict
            // filtered check (a trashed target is not a valid new assignment).
            if (targetIds.Count > 0)
            {
                var found = includeDeleted
                    ? await repository.QueryWhereInWithDeletedAsync(desc.TargetCollection, "id", targetIds, ct)
                    : await repository.QueryWhereInAsync(desc.TargetCollection, "id", targetIds, ct);
                if (found.Count != targetIds.Count)
                    throw new QueryException(
                        $"One or more ids in '{desc.RelationName}' do not exist in '{desc.TargetCollection}'.");
            }

            await repository.SyncManyToManyAsync(
                desc.JunctionType,
                desc.ParentFkProperty,
                desc.TargetFkProperty,
                desc.SortProperty,
                parentId,
                targetIds,
                ct);
        }
    }
}
