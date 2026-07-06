// src/Struo.Application/Query/ItemService.cs
using System.Text.Json;
using Struo.Application.Configuration;
using Struo.Application.Localization;
using Struo.Application.Metadata;
using Struo.Application.Security;
using Struo.Domain.Localization;
using Struo.Domain.Metadata.Enums;
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
    StruoQueryOptions options,
    IHtmlSanitizer sanitizer)
{
    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

    public async Task<PagedResult> QueryAsync(
        string collection, QueryModel raw, string? locale = null, CancellationToken ct = default)
    {
        var meta = Meta(collection);
        if (!permissions.CanRead(collection)) throw new PermissionDeniedException("Read not permitted.");
        ValidateLocale(locale);

        // Compute the effective query locale: explicit locale ?? collection default ?? global default.
        // Only meaningful when the collection has a translation sidecar.
        var queryLocale = meta.Translation is not null
            ? (locale ?? languages.DefaultCode())
            : null;

        var validated = QueryValidator.Validate(raw, meta, options, graph, metadata);
        validated = validated with
        {
            Filter = await relationFilter.RewriteAsync(collection, validated.Filter, queryLocale, ct)
        };
        var searchable = QueryValidator.SearchableFields(meta);
        var result = await repository.QueryAsync(collection, validated, searchable, queryLocale, ct);

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
        if (!permissions.CanRead(collection)) throw new PermissionDeniedException("Read not permitted.");
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
        if (locale is null) return;
        // Charset guard: reject ill-formed locale codes before the IsEnabled membership check
        // so a crafted code containing SQL metacharacters (e.g. a single quote) is stopped here
        // rather than reaching the sort-subquery literal in SqlSugarItemRepository.
        if (!LocaleFormat.IsValid(locale))
            throw new QueryException(
                $"Locale '{locale}' contains invalid characters. Codes must match [A-Za-z0-9_-]{{1,35}}.");
        if (!languages.IsEnabled(locale))
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

        // --- per-locale image resolution (Phase 5.6) ---
        var imageFields = meta.Fields
            .Where(f => f.Translatable && f.Interface is FieldInterface.Image or FieldInterface.File or FieldInterface.Files)
            .Select(f => f.Name)
            .ToList();

        if (imageFields.Count > 0)
        {
            var imageIds = grouped.Values
                .SelectMany(byLocale => byLocale.Values)
                .SelectMany(fieldMap => imageFields
                    .Where(fn => fieldMap.TryGetValue(fn, out var v) && v is Guid)
                    .Select(fn => (object)(Guid)fieldMap[fn]!))
                .Distinct()
                .ToList();

            var byId = new Dictionary<Guid, IReadOnlyDictionary<string, object?>>();
            if (imageIds.Count > 0)
            {
                var files = await repository.QueryWhereInAsync("file", "id", imageIds, ct);
                foreach (var f in files)
                {
                    var projected = ProjectFor("file", f, null);
                    if (projected.TryGetValue("id", out var idVal) && idVal is Guid g)
                        byId[g] = projected;
                }
            }

            foreach (var byLocale in grouped.Values)
                foreach (var fieldMap in byLocale.Values)
                    foreach (var fn in imageFields)
                    {
                        if (!fn.EndsWith("Id", StringComparison.Ordinal)) continue; // only resolve "<name>Id" image fields → "<name>"
                        var resolvedKey = fn[..^2];
                        if (!fieldMap.TryGetValue(fn, out var raw) || raw is not Guid g)
                        {
                            fieldMap[resolvedKey] = null;
                            continue;
                        }
                        fieldMap[resolvedKey] = byId.TryGetValue(g, out var file) ? (object?)file : null;
                    }
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
        if (!permissions.CanWrite(collection)) throw new PermissionDeniedException("Write not permitted.");
        RequireSuperAdminForAdminOnly(meta);
        ValidateLanguageCodeIfNeeded(collection, body);
        var entity = Deserialize(collection, body, meta);
        var d = registry.Get(collection)!;

        // Parent row + M2M junctions + translation sidecars must commit together or not at all —
        // otherwise a failure after the parent insert leaves a row that violates invariants the API
        // enforces (e.g. "default-locale translation required").
        object created = null!;
        await repository.InTransactionAsync(async () =>
        {
            created = await repository.CreateAsync(collection, entity, ct);
            var createdId = d.EntityType.GetProperty(d.IdProperty)!.GetValue(created)!;
            await SyncM2MAsync(collection, body, createdId, ct);
            await SyncTranslationsAsync(meta, body, createdId, isCreate: true, ct);
        }, ct);
        InvalidateLanguagesIfNeeded(collection);
        return Project(created, meta, null);
    }

    public async Task<IReadOnlyDictionary<string, object?>?> UpdateAsync(string collection, string id, JsonElement body, CancellationToken ct = default)
    {
        var meta = Meta(collection);
        if (!permissions.CanWrite(collection)) throw new PermissionDeniedException("Write not permitted.");
        RequireSuperAdminForAdminOnly(meta);
        ValidateLanguageCodeIfNeeded(collection, body);
        var d = registry.Get(collection)!;

        // Merge update: start from the existing row and overlay ONLY the fields the client actually
        // sent (writable, non-readonly, non-system). This preserves server-managed columns the client
        // never sends — e.g. a File's StorageKey/FileName/Size — so a partial PUT (status + translations
        // only) cannot wipe NOT NULL metadata. Relations/translations are synced separately below.
        var existing = await repository.GetByIdAsync(collection, id, ct);
        if (existing is null) return null;
        var incoming = Deserialize(collection, body, meta);
        var bodyKeys = body.ValueKind == JsonValueKind.Object
            ? body.EnumerateObject().Select(p => p.Name).ToHashSet(StringComparer.OrdinalIgnoreCase)
            : new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var field in meta.Fields)
        {
            if (field.IsSystem || field.ReadOnly) continue;
            if (!bodyKeys.Contains(field.Name)) continue;
            if (!d.FieldToProperty.TryGetValue(field.Name, out var prop)) continue;
            var pi = d.EntityType.GetProperty(prop);
            if (pi is not { CanWrite: true }) continue;
            pi.SetValue(existing, pi.GetValue(incoming));
        }

        // Relation foreign keys (M2O / self-referencing tree) are declared via [CmsRelation], not
        // [CmsField], so the field overlay above skips them. Overlay each local FK the client
        // actually sent, keeping the same allowlist discipline (only declared relations, only keys
        // present in the body). Restricted to ManyToOne: O2M relations also carry a non-null
        // ForeignKey (it names the FK column on the *related* entity, not this one), so without
        // this guard the loop would try to overlay a property that doesn't exist/isn't writable
        // here; M2M has no local FK at all.
        foreach (var rel in meta.Relations)
        {
            if (rel.Kind != RelationKind.ManyToOne || rel.ForeignKey is null) continue;
            if (!bodyKeys.Contains(rel.ForeignKey)) continue;  // only overlay when the client sent this FK
            // ForeignKey is stored camelCase (e.g. "categoryId") but the CLR property is PascalCase
            // (e.g. CategoryId), so the lookup must be case-insensitive.
            var pi = d.EntityType.GetProperty(rel.ForeignKey,
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance |
                System.Reflection.BindingFlags.IgnoreCase);
            if (pi is not { CanWrite: true }) continue;        // FK must be a writable property on THIS entity
            pi.SetValue(existing, pi.GetValue(incoming));
        }

        // Optimistic concurrency (D2): guard the write on the version the client last read. When the
        // client echoes `version`, the repository's compare-and-swap rejects the update (409) if another
        // writer already moved the row on. Absent a client version we fall back to the freshly-loaded
        // value (no protection, but backward compatible for callers that don't track versions).
        if (existing is Struo.Domain.Auditing.AuditableEntity ex)
            ex.Version = TryReadVersion(body) ?? ex.Version;

        // Parent row + M2M + translations commit atomically (see CreateAsync).
        object? updated = null;
        await repository.InTransactionAsync(async () =>
        {
            updated = await repository.UpdateAsync(collection, id, existing, ct);
            if (updated is null) return;
            var updatedId = d.EntityType.GetProperty(d.IdProperty)!.GetValue(updated)!;
            await SyncM2MAsync(collection, body, updatedId, ct);
            await SyncTranslationsAsync(meta, body, updatedId, isCreate: false, ct);
        }, ct);
        if (updated is null) return null;
        InvalidateLanguagesIfNeeded(collection);
        return Project(updated, meta, null);
    }

    private void InvalidateLanguagesIfNeeded(string collection)
    {
        if (string.Equals(collection, "language", StringComparison.OrdinalIgnoreCase))
            languages.Invalidate();
    }

    /// <summary>
    /// When writing to the <c>language</c> collection, validates that the <c>code</c> field in
    /// <paramref name="body"/> matches the strict locale-format whitelist so that a crafted code
    /// value cannot later be used to inject SQL via the translatable sort subquery.
    /// </summary>
    private static void ValidateLanguageCodeIfNeeded(string collection, JsonElement body)
    {
        if (!string.Equals(collection, "language", StringComparison.OrdinalIgnoreCase)) return;
        if (!body.TryGetProperty("code", out var codeElem)) return;
        var code = codeElem.ValueKind == JsonValueKind.String ? codeElem.GetString() : null;
        if (code is null) return; // missing/null code handled by required-field validation
        if (!LocaleFormat.IsValid(code))
            throw new QueryException(
                $"Language code '{code}' contains invalid characters. Codes must match [A-Za-z0-9_-]{{1,35}}.");
    }

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
    private async Task SyncTranslationsAsync(
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
                var value = JsonValue(field.Value);
                if (value is string s && IsRichTextField(meta, field.Name))
                    value = SanitizeRichText(s);
                fieldValues[field.Name] = value;
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
    /// Sanitizes a RichText field value: null stays null; otherwise the HTML is run through the
    /// sanitizer and, if the cleaned result is visually blank (no text and no void media), coerced
    /// to null so blank editor documents (<c>&lt;p&gt;&lt;/p&gt;</c>) do not create dirty rows and
    /// so a required RichText field treats blank as missing.
    /// </summary>
    private string? SanitizeRichText(string? raw)
    {
        if (raw is null) return null;
        var clean = sanitizer.Sanitize(raw);
        return IsBlankHtml(clean) ? null : clean;
    }

    private static bool IsBlankHtml(string html)
    {
        if (string.IsNullOrWhiteSpace(html)) return true;
        // Void/media content counts as non-blank.
        if (html.Contains("<img", StringComparison.OrdinalIgnoreCase) ||
            html.Contains("<hr", StringComparison.OrdinalIgnoreCase)) return false;
        // Strip tags and non-breaking spaces; blank if nothing meaningful remains.
        var text = System.Text.RegularExpressions.Regex.Replace(html, "<[^>]+>", string.Empty)
            .Replace("&nbsp;", " ", StringComparison.OrdinalIgnoreCase);
        return string.IsNullOrWhiteSpace(text);
    }

    private static bool IsRichTextField(CollectionMetadata meta, string fieldName) =>
        meta.Fields.Any(f =>
            string.Equals(f.Name, fieldName, StringComparison.OrdinalIgnoreCase) &&
            f.Interface == FieldInterface.RichText);

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
                .Select(e => e.ValueKind == JsonValueKind.Number
                    ? (object)e.GetInt64()
                    : (object)(e.GetString() ?? string.Empty))
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
        var meta = Meta(collection);
        if (!permissions.CanDelete(collection)) throw new PermissionDeniedException("Delete not permitted.");
        RequireSuperAdminForAdminOnly(meta);

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
            var typedId = IdParsing.ParseTo(id, pkProp.PropertyType);

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

    // Reads the optimistic-concurrency token the client echoed back, if any. Accepts a JSON number or
    // a numeric string; returns null when absent or unparseable (treated as "no version supplied").
    private static long? TryReadVersion(JsonElement body)
    {
        if (body.ValueKind != JsonValueKind.Object) return null;
        if (!body.TryGetProperty("version", out var v)) return null;
        return v.ValueKind switch
        {
            JsonValueKind.Number when v.TryGetInt64(out var n) => n,
            JsonValueKind.String when long.TryParse(v.GetString(), out var n) => n,
            _ => null
        };
    }

    // Writes to an AdminOnly collection (identity/authorization tables: user/role/permission/userRole)
    // require a super-admin even when the caller holds a per-collection write/delete grant. Otherwise a
    // delegated grant on, say, userRole could be used to self-assign the super-admin role — privilege
    // escalation. Reads are intentionally not gated here (ordinary RBAC governs them).
    private void RequireSuperAdminForAdminOnly(CollectionMetadata meta)
    {
        if (meta.AdminOnly && !permissions.IsSuperAdmin)
            throw new PermissionDeniedException($"Writes to '{meta.Name}' require a super-admin.");
    }

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

        // Sanitize non-translatable RichText field values on the entity before required validation,
        // so stored HTML is XSS-clean and a blank editor document is treated as missing.
        foreach (var field in meta.Fields.Where(f => f.Interface == FieldInterface.RichText && !f.Translatable))
        {
            if (!d.FieldToProperty.TryGetValue(field.Name, out var prop)) continue;
            var pi = d.EntityType.GetProperty(prop);
            if (pi is not { CanWrite: true } || pi.PropertyType != typeof(string)) continue;
            if (pi.GetValue(entity) is string raw)
                pi.SetValue(entity, SanitizeRichText(raw));
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

        // Always expose the concurrency token (like id, independent of field selection) so the client
        // can echo it back on update for optimistic-locking (D2).
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
            dict[field.Name] = d.EntityType.GetProperty(prop)?.GetValue(entity);
        }
        return dict;
    }
}
