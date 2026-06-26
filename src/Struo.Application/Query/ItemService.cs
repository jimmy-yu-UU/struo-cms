// src/Struo.Application/Query/ItemService.cs
using System.Text.Json;
using Struo.Application.Configuration;
using Struo.Application.Localization;
using Struo.Application.Metadata;
using Struo.Application.Security;
using Struo.Domain.Metadata.Models;
using Struo.Domain.Query;

namespace Struo.Application.Query;

public sealed record PagedResult(
    IReadOnlyList<IReadOnlyDictionary<string, object?>> Data, int Total, int Limit, int Offset);

public sealed class ItemService(
    IItemRepository repository,
    IMetadataProvider metadata,
    IEntityRegistry registry,
    IPermissionService permissions,
    IRelationshipGraph graph,
    IRelationExpander expander,
    IM2MDescriptorSource m2mSource,
    IRelationFilterResolver relationFilter,
    ILanguageProvider languages,
    StruoQueryOptions options)
{
    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

    public async Task<PagedResult> QueryAsync(
        string collection, QueryModel raw, string? locale = null, CancellationToken ct = default)
    {
        var meta = Meta(collection);
        if (!permissions.CanRead(collection)) throw new QueryException("Read not permitted.");
        ValidateLocale(locale);
        var validated = QueryValidator.Validate(raw, meta, options, graph, metadata);
        validated = validated with { Filter = await relationFilter.RewriteAsync(collection, validated.Filter, ct) };
        var searchable = QueryValidator.SearchableFields(meta);
        var result = await repository.QueryAsync(collection, validated, searchable, ct);

        var entities = result.Rows;
        var rows = entities.Select(r => Project(r, meta, validated.Fields)).ToList();
        await ExpandDeepAsync(collection, raw.Deep, entities, rows, ct);
        await OverlayTranslationsAsync(meta, entities, rows, locale, ct);
        return new PagedResult(rows, result.Total, validated.Limit, validated.Offset);
    }

    public async Task<IReadOnlyDictionary<string, object?>?> GetAsync(
        string collection, string id, DeepSpec? deep = null, string? locale = null, CancellationToken ct = default)
    {
        var meta = Meta(collection);
        if (!permissions.CanRead(collection)) throw new QueryException("Read not permitted.");
        ValidateLocale(locale);
        var entity = await repository.GetByIdAsync(collection, id, ct);
        if (entity is null) return null;

        var projected = (Dictionary<string, object?>)Project(entity, meta, null);
        await ExpandDeepAsync(collection, deep, [entity], [projected], ct);
        await OverlayTranslationsAsync(meta, [entity], [projected], locale, ct);
        return projected;
    }

    private void ValidateLocale(string? locale)
    {
        if (locale is not null && !languages.IsEnabled(locale))
            throw new QueryException($"Unknown or disabled locale '{locale}'.");
    }

    /// <summary>
    /// When <paramref name="meta"/> declares a translation sidecar, batch-loads the translation
    /// rows for all parent ids (optionally filtered to <paramref name="locale"/>), groups them by
    /// the parent FK, and attaches a <c>translations</c> map
    /// (<c>{ locale: { camelField: value } }</c>) onto each projected row.
    /// </summary>
    private async Task OverlayTranslationsAsync(
        CollectionMetadata meta,
        IReadOnlyList<object> entities,
        IReadOnlyList<IReadOnlyDictionary<string, object?>> rows,
        string? locale,
        CancellationToken ct)
    {
        var tm = meta.Translation;
        if (tm is null || entities.Count == 0) return;

        var d = registry.Get(meta.Name)!;
        var idProp = d.IdProperty;

        var ids = entities
            .Select(e => ReadProp(e, idProp))
            .Where(v => v is not null)
            .Select(v => v!)
            .Distinct()
            .ToList();
        if (ids.Count == 0) return;

        var tRows = await repository.LoadTranslationsAsync(
            tm.TranslationEntityType, tm.ForeignKeyProperty, tm.LocaleProperty, ids, locale, ct);

        // Map camelCase translatable field -> CLR property name on the translation entity.
        var camelToClr = tm.Fields.ToDictionary(
            f => f,
            f => tm.TranslationEntityType
                     .GetProperty(f, System.Reflection.BindingFlags.Public |
                                     System.Reflection.BindingFlags.Instance |
                                     System.Reflection.BindingFlags.IgnoreCase)?.Name ?? f,
            StringComparer.OrdinalIgnoreCase);

        // Group translation rows by parent id (string-keyed for cross-type comparison safety).
        var grouped = new Dictionary<string, Dictionary<string, Dictionary<string, object?>>>();
        foreach (var tr in tRows)
        {
            var fk = ReadProp(tr, tm.ForeignKeyProperty);
            var loc = ReadProp(tr, tm.LocaleProperty) as string;
            if (fk is null || loc is null) continue;
            var key = fk.ToString()!;
            if (!grouped.TryGetValue(key, out var byLocale))
                grouped[key] = byLocale = new Dictionary<string, Dictionary<string, object?>>();

            var fieldMap = new Dictionary<string, object?>();
            foreach (var (camel, clr) in camelToClr)
                fieldMap[camel] = ReadProp(tr, clr);
            byLocale[loc] = fieldMap;
        }

        for (var i = 0; i < entities.Count; i++)
        {
            var pid = ReadProp(entities[i], idProp)?.ToString();
            var dict = (Dictionary<string, object?>)rows[i];
            dict["translations"] = pid is not null && grouped.TryGetValue(pid, out var byLocale)
                ? byLocale
                : new Dictionary<string, Dictionary<string, object?>>();
        }
    }

    /// <summary>
    /// Validates the requested deep relations against the relationship graph and the
    /// configured <see cref="StruoQueryOptions.MaxRelationDepth"/>, then nests the expanded
    /// (batched) relation rows into each projected parent dictionary. No-op when
    /// <paramref name="deep"/> is null/empty or there are no parent rows.
    /// </summary>
    private async Task ExpandDeepAsync(
        string collection, DeepSpec? deep,
        IReadOnlyList<object> entities, IReadOnlyList<IReadOnlyDictionary<string, object?>> rows,
        CancellationToken ct)
    {
        if (deep is null || deep.Relations.Count == 0 || entities.Count == 0) return;

        // Validate: deep expansion is single-level here, so MaxRelationDepth caps the number
        // of relations expanded in one request (each adds one level of nesting / one batch query).
        if (deep.Relations.Count > options.MaxRelationDepth)
            throw new QueryException(
                $"Too many deep relations requested ({deep.Relations.Count}); the maximum is {options.MaxRelationDepth}.");
        foreach (var relName in deep.Relations.Keys)
        {
            if (graph.Resolve(collection, relName) is null)
                throw new QueryException($"Unknown relation '{relName}' on '{collection}'.");
        }

        var parentDesc = registry.Get(collection)!;

        object ParentId(object entity) =>
            ReadProp(entity, parentDesc.IdProperty)
            ?? throw new QueryException($"Cannot expand relations: a '{collection}' row has no id.");

        var nested = await expander.ExpandAsync(
            collection, entities, deep, ProjectFor, ParentId, ReadProp, ct);

        for (var i = 0; i < entities.Count; i++)
        {
            var pid = ParentId(entities[i]);
            if (!nested.TryGetValue(pid, out var relMap)) continue;
            var dict = (Dictionary<string, object?>)rows[i];
            foreach (var (relName, value) in relMap) dict[relName] = value;
        }
    }

    /// <summary>Reuses the metadata projection for an arbitrary target collection.</summary>
    private IReadOnlyDictionary<string, object?> ProjectFor(
        string collectionName, object entity, IReadOnlyList<string>? fields) =>
        Project(entity, Meta(collectionName), fields);

    /// <summary>
    /// Reads a property value off an entity by name. The name may be a CLR property name
    /// (e.g. junction <c>ArticleId</c>) or a camelCase field name (e.g. <c>authorId</c>, <c>id</c>),
    /// so the lookup is case-insensitive.
    /// </summary>
    private static object? ReadProp(object entity, string propertyName) =>
        entity.GetType()
            .GetProperty(propertyName,
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance |
                System.Reflection.BindingFlags.IgnoreCase)
            ?.GetValue(entity);

    public async Task<IReadOnlyDictionary<string, object?>> CreateAsync(string collection, JsonElement body, CancellationToken ct = default)
    {
        var meta = Meta(collection);
        if (!permissions.CanWrite(collection)) throw new QueryException("Write not permitted.");
        var entity = Deserialize(collection, body, meta);
        var created = await repository.CreateAsync(collection, entity, ct);
        var d = registry.Get(collection)!;
        var createdId = d.EntityType.GetProperty(d.IdProperty)!.GetValue(created)!;
        await SyncM2MAsync(collection, body, createdId, ct);
        await SyncTranslationsAsync(meta, body, createdId, isCreate: true, ct);
        InvalidateLanguagesIfNeeded(collection);
        return Project(created, meta, null);
    }

    public async Task<IReadOnlyDictionary<string, object?>?> UpdateAsync(string collection, string id, JsonElement body, CancellationToken ct = default)
    {
        var meta = Meta(collection);
        if (!permissions.CanWrite(collection)) throw new QueryException("Write not permitted.");
        var entity = Deserialize(collection, body, meta);
        var updated = await repository.UpdateAsync(collection, id, entity, ct);
        if (updated is null) return null;
        var d = registry.Get(collection)!;
        var updatedId = d.EntityType.GetProperty(d.IdProperty)!.GetValue(updated)!;
        await SyncM2MAsync(collection, body, updatedId, ct);
        await SyncTranslationsAsync(meta, body, updatedId, isCreate: false, ct);
        InvalidateLanguagesIfNeeded(collection);
        return Project(updated, meta, null);
    }

    private void InvalidateLanguagesIfNeeded(string collection)
    {
        if (string.Equals(collection, "language", StringComparison.OrdinalIgnoreCase))
            languages.Invalidate();
    }

    /// <summary>
    /// When <paramref name="meta"/> declares a translation sidecar and the request body carries a
    /// <c>translations</c> object, validates each locale (enabled), each field key (⊆ translatable
    /// fields), and required fields, then delegates to
    /// <see cref="IItemRepository.SyncTranslationsAsync"/>. On create the default-locale translation
    /// must be present. Absent <c>translations</c> key is skipped (partial updates supported).
    /// </summary>
    private async Task SyncTranslationsAsync(
        CollectionMetadata meta, JsonElement body, object parentId, bool isCreate, CancellationToken ct)
    {
        var tm = meta.Translation;
        if (tm is null) return;
        if (!body.TryGetProperty("translations", out var trElem)) return;
        if (trElem.ValueKind != JsonValueKind.Object) return;

        var allowed = tm.Fields.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var requiredFields = meta.Fields
            .Where(f => f.Translatable && f.Required)
            .Select(f => f.Name)
            .ToList();

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
                fieldValues[field.Name] = JsonValue(field.Value);
            }

            // Required: every required translatable field must be present and non-empty.
            foreach (var rf in requiredFields)
            {
                var has = fieldValues.TryGetValue(rf, out var v);
                if (!has || v is null || (v is string s && string.IsNullOrWhiteSpace(s)))
                    throw new QueryException(
                        $"Required translation field '{rf}' is missing for locale '{locale}'.");
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

    /// <summary>Converts a JSON value to a CLR primitive for translation field storage.</summary>
    private static object? JsonValue(JsonElement e) => e.ValueKind switch
    {
        JsonValueKind.String => e.GetString(),
        JsonValueKind.Number => e.TryGetInt64(out var l) ? l : e.GetDouble(),
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        JsonValueKind.Null => null,
        _ => e.GetRawText()
    };

    /// <summary>
    /// For each M2M relation declared on <paramref name="collection"/>, reads the target-id array
    /// from <paramref name="body"/> under the relation's camelCase name (e.g. <c>"tags"</c>).
    /// If the key is present, validates every id exists in the target collection, then delegates
    /// to <see cref="IItemRepository.SyncManyToManyAsync"/> to replace the junction rows.
    /// Absent keys are silently skipped (partial updates are supported).
    /// </summary>
    private async Task SyncM2MAsync(string collection, JsonElement body, object parentId, CancellationToken ct)
    {
        var descs = m2mSource.M2MDescriptors(collection);
        if (descs.Count == 0) return;

        foreach (var desc in descs)
        {
            // Only sync when the relation key is present in the request body.
            if (!body.TryGetProperty(desc.RelationName, out var idsElem)) continue;
            if (idsElem.ValueKind != System.Text.Json.JsonValueKind.Array) continue;

            var targetIds = idsElem.EnumerateArray()
                .Select(e => (object)e.GetInt64())
                .ToList();

            // Validate all target ids exist.
            if (targetIds.Count > 0)
            {
                var found = await repository.QueryWhereInAsync(desc.TargetCollection, "id", targetIds, ct);
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

    public async Task<bool> DeleteAsync(string collection, string id, CancellationToken ct = default)
    {
        _ = Meta(collection);
        if (!permissions.CanDelete(collection)) throw new QueryException("Delete not permitted.");

        // Enforce OnDelete.Restrict: for each inbound M2O relation with Restrict semantics,
        // check whether any row in the source collection still references this id.
        var inbound = graph.InboundRestrict(collection);
        if (inbound.Count > 0)
        {
            // Coerce the string id to the PK's CLR type ONCE so QueryWhereInAsync receives a
            // typed value that matches the FK column. Fail loudly on misconfiguration / bad id —
            // a silent fallback would make the IN comparison miss and skip a real Restrict block.
            var targetDesc = registry.Get(collection) ?? throw new CollectionNotFoundException(collection);
            var pkProp = targetDesc.EntityType.GetProperty(targetDesc.IdProperty,
                             System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance)
                         ?? throw new InvalidOperationException(
                             $"Collection '{collection}' has no primary-key property '{targetDesc.IdProperty}'.");
            object typedId;
            try { typedId = Convert.ChangeType(id, pkProp.PropertyType); }
            catch (Exception ex) when (ex is FormatException or InvalidCastException or OverflowException)
            { throw new QueryException($"Invalid id '{id}' for collection '{collection}'."); }

            foreach (var (sourceCollection, foreignKey) in inbound)
            {
                var refs = await repository.QueryWhereInAsync(sourceCollection, foreignKey, [typedId], ct);
                if (refs.Count > 0)
                    throw new RelationConflictException(
                        $"Cannot delete '{collection}/{id}': referenced by '{sourceCollection}'.");
            }
        }

        return await repository.DeleteAsync(collection, id, ct);
    }

    /// <summary>
    /// Serialises <paramref name="source"/> as UTF-8 JSON with any top-level key in
    /// <paramref name="keysToRemove"/> omitted. Returns the raw bytes so the caller can
    /// parse them into a <c>using</c>-scoped <see cref="JsonDocument"/> and avoid a pool leak.
    /// </summary>
    private static byte[] StripKeys(JsonElement source, IReadOnlySet<string> keysToRemove)
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

    private CollectionMetadata Meta(string collection) =>
        metadata.GetCollection(collection) ?? throw new CollectionNotFoundException(collection);

    private object Deserialize(string collection, JsonElement body, CollectionMetadata meta)
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

        object entity;
        if (stripNames.Count > 0)
        {
            // Parse into a using-scoped document so the ArrayPool buffer is returned promptly.
            using var stripped = JsonDocument.Parse(StripKeys(body, stripNames));
            entity = stripped.RootElement.Deserialize(d.EntityType, JsonOpts)
                     ?? throw new QueryException("Request body could not be parsed.");
        }
        else
        {
            entity = body.Deserialize(d.EntityType, JsonOpts)
                     ?? throw new QueryException("Request body could not be parsed.");
        }

        // strip system/read-only fields (audit AOP / identity own them); only nullable props can be nulled
        foreach (var field in meta.Fields.Where(f => f.IsSystem || f.ReadOnly))
        {
            if (!d.FieldToProperty.TryGetValue(field.Name, out var prop)) continue;
            var pi = d.EntityType.GetProperty(prop);
            if (pi is not { CanWrite: true }) continue;
            var canBeNull = !pi.PropertyType.IsValueType || Nullable.GetUnderlyingType(pi.PropertyType) is not null;
            if (canBeNull) pi.SetValue(entity, null);
        }

        // required validation — skip translatable fields (they live on the sidecar entity and
        // are validated per-locale in SyncTranslationsAsync, not on the parent).
        foreach (var field in meta.Fields.Where(f => f.Required && !f.Translatable))
        {
            var pi = d.FieldToProperty.TryGetValue(field.Name, out var prop) ? d.EntityType.GetProperty(prop) : null;
            var value = pi?.GetValue(entity);
            if (value is null || (value is string s && string.IsNullOrWhiteSpace(s)))
                throw new QueryException($"Field '{field.Name}' is required.");
        }
        return entity;
    }

    private IReadOnlyDictionary<string, object?> Project(object entity, CollectionMetadata meta, IReadOnlyList<string>? fields)
    {
        var d = registry.Get(meta.Name)!;
        var dict = new Dictionary<string, object?>();

        const string idKey = "id";
        dict[idKey] = d.EntityType.GetProperty(d.IdProperty)?.GetValue(entity);

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
            dict[field.Name] = d.EntityType.GetProperty(prop)?.GetValue(entity);
        }
        return dict;
    }
}
