// src/Struo.Application/Query/Write/ItemWriteSideSync.cs
using System.Text.Json;
using Struo.Application.Localization;
using Struo.Application.Metadata;
using Struo.Application.Query.Write;
using Struo.Application.Security;
using Struo.Domain.Metadata.Models;
using Struo.Domain.Query;

namespace Struo.Application.Query;

/// <summary>
/// Write-side relation/translation synchronization, extracted verbatim from <see cref="ItemService"/>.
/// Both methods run INSIDE the caller's write transaction (see
/// <see cref="ItemService.CreateAsync"/> / <c>UpdateCoreAsync</c>), so a failure here rolls the whole
/// write back. Validation phase order and exception message strings are observable and preserved exactly.
/// </summary>
public sealed class ItemWriteSideSync(
    IItemRepository repository,
    IM2MDescriptorSource m2mSource,
    ILanguageProvider languages,
    RichTextCleaner richText,
    ItemDeserializer deserializer,
    IMetadataProvider metadata,
    IPermissionService permissions)
{
    /// <summary>
    /// When <paramref name="meta"/> declares a translation sidecar and the request body carries a
    /// <c>translations</c> object, validates each locale (enabled), each field key (⊆ translatable
    /// fields), and required fields, then delegates to
    /// <see cref="IItemRepository.SyncTranslationsAsync"/>.
    /// <para>
    /// On create (<paramref name="isCreate"/> = true) the default-locale translation is mandatory:
    /// an absent <c>translations</c> key, a non-object value, an empty object, or an
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
        // `translations` key would silently create a row with zero translation rows.
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

            // Max length — a CMS-layer limit, measured after RichText sanitization.
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
    /// to <see cref="IItemRepository.SyncManyToManyAsync"/> to diff-and-patch the junction rows
    /// (existing rows for targets that remain keep their primary key; only the added/removed set
    /// actually changes). Absent keys are silently skipped (partial updates are supported).
    /// </summary>
    public async Task SyncM2MAsync(string collection, JsonElement body, object parentId, bool includeDeleted, CancellationToken ct)
    {
        var descs = m2mSource.M2MDescriptors(collection);
        if (descs.Count == 0) return;

        foreach (var desc in descs)
        {
            // Only sync when the relation key is present in the request body.
            if (!body.TryGetProperty(desc.RelationName, out var idsElem)) continue;
            if (idsElem.ValueKind != JsonValueKind.Array) continue;

            var hasObjectElement = idsElem.EnumerateArray().Any(e => e.ValueKind == JsonValueKind.Object);
            var (junctionMeta, payloadNames) = EnsureJunctionPayloadGrant(desc, hasObjectElement);

            var links = ParseLinks(idsElem, desc, junctionMeta, payloadNames);
            var targetIds = links.Select(l => l.TargetId).ToList();

            await ValidateTargetsExistAsync(desc, targetIds, includeDeleted, ct);

            await repository.SyncManyToManyAsync(
                desc.JunctionType,
                desc.ParentFkProperty,
                desc.TargetFkProperty,
                desc.SortProperty,
                parentId,
                links,
                ct);
        }
    }

    // An object element carries junction payload — gate it BEFORE any parsing/binding runs, so a
    // caller who lacks the grant gets a 403, not a 400 from probing the payload's own field
    // rules (e.g. a MaxLength violation). Resolved lazily: only when the array actually contains
    // an object element, and only for a relation whose junction declares payload at all (a
    // relation without payload rejects any object element as an invalid id regardless of grant —
    // unchanged, existing behaviour).
    private (CollectionMetadata? JunctionMeta, HashSet<string>? PayloadNames) EnsureJunctionPayloadGrant(
        M2MDescriptor desc, bool hasObjectElement)
    {
        if (!hasObjectElement || !desc.HasPayload) return (null, null);

        var junctionMeta = metadata.GetCollection(desc.JunctionCollection!)
            ?? throw new CollectionNotFoundException(desc.JunctionCollection!);
        // AdminOnly junctions require a super-admin regardless of any per-collection write grant
        // — the same escalation guard ItemService.RequireSuperAdminForAdminOnly applies to every
        // other write path (a fork hanging an AdminOnly junction off a non-AdminOnly parent must
        // not gain a second, weaker write surface into it).
        if (junctionMeta.AdminOnly && !permissions.IsSuperAdmin)
            throw new PermissionDeniedException($"Writes to '{junctionMeta.Name}' require a super-admin.");
        if (!permissions.CanWrite(desc.JunctionCollection!))
            throw new PermissionDeniedException($"Write to '{desc.JunctionCollection}' not permitted.");
        var payloadNames = desc.JunctionPayload!.Select(p => p.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return (junctionMeta, payloadNames);
    }

    // Ordered, id-keyed merge of the array elements. `tags:[t1,t1]` is semantically `tags:[t1]`
    // (a junction is a set) — de-duplicating up front also matters for the count-based existence
    // check below and for the repository, which throws on a duplicate TargetId. An element may be
    // a bare id (membership only) or, on a relation that declares payload, an object carrying an
    // `id` plus payload fields; when the same id appears more than once, an object always
    // outranks a bare id (whichever came first or last), and between two objects the later one
    // wins. Order of first appearance is the sort order.
    //
    // ParseLinkElement below coerces each element: a Number becomes a bare integer id, a String a
    // bare string id, and — on a relation with payload — an Object carrying an `id` plus payload
    // fields becomes a JunctionLink with the bound payload. Any other shape (bool, array, a
    // non-integer number, or an Object where the relation has no payload) is rejected as a
    // QueryException (-> 400) rather than reaching the repository.
    private List<JunctionLink> ParseLinks(
        JsonElement idsElem, M2MDescriptor desc, CollectionMetadata? junctionMeta, HashSet<string>? payloadNames)
    {
        var order = new List<object>();
        var merged = new Dictionary<object, JunctionLink>();
        foreach (var e in idsElem.EnumerateArray())
        {
            var link = ParseLinkElement(e, desc, junctionMeta, payloadNames);
            if (!merged.ContainsKey(link.TargetId)) order.Add(link.TargetId);
            if (merged.TryGetValue(link.TargetId, out var prev) && link.Payload is null && prev.Payload is not null)
                continue; // an object already recorded outranks a later bare id
            merged[link.TargetId] = link;
        }
        return order.Select(id => merged[id]).ToList();
    }

    private JunctionLink ParseLinkElement(
        JsonElement e, M2MDescriptor desc, CollectionMetadata? junctionMeta, HashSet<string>? payloadNames)
    {
        switch (e.ValueKind)
        {
            case JsonValueKind.Number when e.TryGetInt64(out var n): return JunctionLink.Bare(n);
            case JsonValueKind.String: return JunctionLink.Bare(e.GetString() ?? string.Empty);
            case JsonValueKind.Object when desc.HasPayload:
            {
                if (!e.TryGetProperty("id", out var idEl))
                    throw new QueryException($"Each object in '{desc.RelationName}' must carry an 'id'.");
                object id = idEl.ValueKind switch
                {
                    JsonValueKind.Number when idEl.TryGetInt64(out var n) => n,
                    JsonValueKind.String => idEl.GetString() ?? string.Empty,
                    _ => throw new QueryException($"One or more ids in '{desc.RelationName}' are not valid."),
                };
                IReadOnlyDictionary<string, object?> bound;
                try
                {
                    bound = deserializer.DeserializePartial(desc.JunctionCollection!, e, junctionMeta!, payloadNames!);
                }
                catch (QueryException ex)
                {
                    // The payload binder reports errors by field name only; a form editing several
                    // links needs to know which relation and which target row the field belonged to.
                    throw new QueryException($"Relation '{desc.RelationName}', target '{id}': {ex.Message}");
                }
                return new JunctionLink(id, bound);
            }
            default:
                throw new QueryException($"One or more ids in '{desc.RelationName}' are not valid.");
        }
    }

    // Validate all target ids exist. A REVERT (includeDeleted) may legitimately reference a
    // target that has since been trashed — the snapshot was captured while it was still live — so
    // it validates against the soft-delete-bypassing query; a normal write keeps the strict
    // filtered check (a trashed target is not a valid new assignment).
    private async Task ValidateTargetsExistAsync(
        M2MDescriptor desc, List<object> targetIds, bool includeDeleted, CancellationToken ct)
    {
        if (targetIds.Count == 0) return;

        var found = includeDeleted
            ? await repository.QueryWhereInWithDeletedAsync(desc.TargetCollection, "id", targetIds, ct)
            : await repository.QueryWhereInAsync(desc.TargetCollection, "id", targetIds, ct);
        if (found.Count != targetIds.Count)
            throw new QueryException(
                $"One or more ids in '{desc.RelationName}' do not exist in '{desc.TargetCollection}'.");
    }
}
