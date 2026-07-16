// src/Struo.Application/Query/ItemService.cs
using System.Text.Json;
using Struo.Application.Abstractions;
using Struo.Application.Configuration;
using Struo.Application.Localization;
using Struo.Application.Metadata;
using Struo.Application.Revisions;
using Struo.Application.Security;
using Struo.Domain.Auditing;
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
    IHtmlSanitizer sanitizer,
    ICurrentUserAccessor currentUser,
    IRevisionStore revisions,
    RevisionSnapshotBuilder snapshotBuilder)
{
    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

    private static readonly HashSet<FieldInterface> MultiValueInterfaces =
    [
        FieldInterface.MultiSelect, FieldInterface.CheckboxGroup, FieldInterface.Tags
    ];

    public async Task<PagedResult> QueryAsync(
        string collection, QueryModel raw, string? locale = null,
        DeletedFilter deleted = DeletedFilter.Exclude, CancellationToken ct = default)
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
        var result = await repository.QueryAsync(collection, validated, searchable, queryLocale, deleted, ct);

        var entities = result.Rows;
        var rows = entities.Select(r => Project(r, meta, validated.Fields)).ToList();
        await ExpandDeepAsync(collection, raw.Deep, entities, rows, queryLocale, ct);
        await OverlayTranslationsAsync(meta, entities, rows, locale, ct);
        return new PagedResult(rows, result.Total, validated.Limit, validated.Offset);
    }

    public async Task<IReadOnlyDictionary<string, object?>?> GetAsync(
        string collection, string id, DeepSpec? deep = null, string? locale = null,
        DeletedFilter deleted = DeletedFilter.Exclude, CancellationToken ct = default)
    {
        var meta = Meta(collection);
        if (!permissions.CanRead(collection)) throw new PermissionDeniedException("Read not permitted.");
        ValidateLocale(locale);
        var entity = await repository.GetByIdAsync(collection, id, deleted, ct);
        if (entity is null) return null;

        var projected = (Dictionary<string, object?>)Project(entity, meta, null);
        var queryLocale = meta.Translation is not null ? (locale ?? languages.DefaultCode()) : null;
        await ExpandDeepAsync(collection, deep, [entity], [projected], queryLocale, ct);
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
        string? locale, CancellationToken ct)
    {
        if (deep is null || deep.Relations.Count == 0) return;

        // Validate the whole nested tree: nesting depth <= MaxRelationDepth, and every relation
        // name resolves against its own level's collection. Runs before any query executes,
        // and independent of row count — an over-depth/unknown-relation request must be
        // rejected even when the parent query matched zero rows.
        ValidateDeepTree(collection, deep, depth: 1);
        if (entities.Count == 0) return;

        var parentDesc = registry.Get(collection)!;

        object ParentId(object entity) =>
            ReadProp(entity, parentDesc.IdProperty)
            ?? throw new QueryException($"Cannot expand relations: a '{collection}' row has no id.");

        var nested = await expander.ExpandAsync(
            collection, entities, deep, ProjectFor, ParentId, ReadProp, locale, ct);

        for (var i = 0; i < entities.Count; i++)
        {
            var pid = ParentId(entities[i]);
            if (!nested.TryGetValue(pid, out var relMap)) continue;
            var dict = (Dictionary<string, object?>)rows[i];
            foreach (var (relName, value) in relMap) dict[relName] = value;
        }
    }

    /// <summary>
    /// Recursively validates a <see cref="DeepSpec"/> tree: throws if nesting depth exceeds
    /// <see cref="StruoQueryOptions.MaxRelationDepth"/> or a relation name is unknown at its level.
    /// Also validates per-level nested-list args (filter/sort/limit/offset): to-many-only,
    /// filter fields whitelisted against the target collection, sort restricted to the target's
    /// own fields (no cross-relation sort for nested lists), and non-negative limit/offset.
    /// </summary>
    private void ValidateDeepTree(string coll, DeepSpec spec, int depth)
    {
        if (depth > options.MaxRelationDepth)
            throw new QueryException(
                $"Relation nesting too deep (depth {depth}); the maximum is {options.MaxRelationDepth}.");
        foreach (var (relName, relSpec) in spec.Relations)
        {
            var rel = graph.Resolve(coll, relName)
                ?? throw new QueryException($"Unknown relation '{relName}' on '{coll}'.");

            var hasArgs = relSpec.Filter is not null || relSpec.Sort is not null
                          || relSpec.Limit is not null || relSpec.Offset is not null;
            if (hasArgs && rel.Kind == RelationKind.ManyToOne)
                throw new QueryException(
                    $"filter/sort/limit/offset are only supported on to-many relations; " +
                    $"'{relName}' on '{coll}' is many-to-one.");

            if (relSpec.Limit is < 0)
                throw new QueryException($"Nested 'limit' must not be negative for relation '{relName}'.");
            if (relSpec.Offset is < 0)
                throw new QueryException($"Nested 'offset' must not be negative for relation '{relName}'.");

            var targetMeta = Meta(rel.TargetCollection);
            if (relSpec.Filter is not null)
                QueryValidator.Validate(
                    new QueryModel(null, relSpec.Filter, [], 0, 0, null),
                    targetMeta, options, graph, metadata);

            if (relSpec.Sort is not null)
                foreach (var s in relSpec.Sort)
                {
                    if (RelationPath.IsRelationPath(s.Field))
                        throw new QueryException(
                            $"Sort across relations is not supported for nested lists: '{s.Field}'.");
                    QueryValidator.Validate(
                        new QueryModel(null, null, [s], 0, 0, null),
                        targetMeta, options, graph, metadata);
                }

            if (relSpec.Deep is not null)
                ValidateDeepTree(rel.TargetCollection, relSpec.Deep, depth + 1);
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
        PropertyAccessorCache.Read(entity, propertyName);

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
            if (meta.Revisions)
            {
                var snapshot = await snapshotBuilder.BuildAsync(collection, created, ct);
                await revisions.CaptureAsync(collection, createdId.ToString()!, "create", snapshot, ct);
            }
        }, ct);
        InvalidateLanguagesIfNeeded(collection);
        return Project(created, meta, null);
    }

    public Task<IReadOnlyDictionary<string, object?>?> UpdateAsync(string collection, string id, JsonElement body, CancellationToken ct = default)
        => UpdateCoreAsync(collection, id, body, "update", ct);

    private async Task<IReadOnlyDictionary<string, object?>?> UpdateCoreAsync(string collection, string id, JsonElement body, string operation, CancellationToken ct)
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
        var existing = await repository.GetByIdAsync(collection, id, ct: ct);
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
            if (meta.Revisions)
            {
                var snapshot = await snapshotBuilder.BuildAsync(collection, updated!, ct);
                await revisions.CaptureAsync(collection, updatedId.ToString()!, operation, snapshot, ct);
            }
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

            // Max length — CMS-layer limit (7g.5), measured after RichText sanitization.
            foreach (var (name, value) in fieldValues)
            {
                if (value is string sv && maxLengths.TryGetValue(name, out var max) && sv.Length > max)
                    throw new QueryException(
                        $"Field '{name}' exceeds maximum length {max} for locale '{locale}'.");
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

    /// <summary>
    /// Deletes an item. For a soft-deletable collection with <paramref name="purge"/> false, this
    /// stamps <c>DeletedAt</c> (trash) — behavior unchanged from before Task 5. Otherwise (an explicit
    /// purge, or a collection with no soft-delete tier at all) this is a permanent removal, and runs
    /// the full referential-integrity pipeline (DB-1/DB-2): SetNull inbound FKs, recursively Cascade
    /// inbound rows through the same core, delete M2M junction rows (both sides), delete translation
    /// sidecar rows, delete revision history, then the row itself — all inside ONE transaction so a
    /// mid-pipeline failure leaves nothing half-deleted.
    /// </summary>
    public async Task<bool> DeleteAsync(string collection, string id, bool purge = false, CancellationToken ct = default)
    {
        var meta = Meta(collection);
        if (!permissions.CanDelete(collection)) throw new PermissionDeniedException("Delete not permitted.");
        RequireSuperAdminForAdminOnly(meta);

        if (meta.SoftDelete && !purge)
        {
            // Restrict still guards the soft-delete (trash) branch — unchanged contract. The purge
            // branch below re-runs the identical check as the first step of PurgeCoreAsync, so every
            // recursively-cascaded row is ALSO Restrict-guarded, not just the top-level target.
            await CheckRestrictAsync(collection, id, ct);
            var actor = currentUser.GetCurrentUserId();

            // DB-8: trash bumps the item's Version (repository, AuditableEntity only) AND — for a
            // revisioned collection — records a "delete" revision. Both must commit together with the
            // trash stamp, so a capture failure rolls the whole trash back (no half-trashed row, no
            // orphan revision).
            var softDeleted = false;
            await repository.InTransactionAsync(async () =>
            {
                softDeleted = await repository.SoftDeleteAsync(collection, id, DateTime.UtcNow, actor, ct);
                if (softDeleted)
                    await CaptureRevisionAsync(collection, id, meta, "delete", ct);
            }, ct);
            return softDeleted;
        }

        var existed = false;
        await repository.InTransactionAsync(async () =>
        {
            existed = await PurgeCoreAsync(collection, id, new HashSet<(string Collection, string Id)>(), ct);
        }, ct);
        return existed;
    }

    /// <summary>
    /// Throws <see cref="RelationConflictException"/> if any inbound OnDelete.Restrict relation on
    /// <paramref name="collection"/> still has a row referencing <paramref name="id"/>. Shared by the
    /// soft-delete (trash) branch and, recursively, by every level of <see cref="PurgeCoreAsync"/>.
    /// </summary>
    private async Task CheckRestrictAsync(string collection, string id, CancellationToken ct)
    {
        var inbound = graph.InboundRestrict(collection);
        if (inbound.Count == 0) return;

        // Coerce the string id to the PK's CLR type ONCE so QueryWhereInAsync receives a typed
        // value that matches the FK column. Fail loudly on misconfiguration / bad id — a silent
        // fallback would make the IN comparison miss and skip a real Restrict block.
        var typedId = TypedId(collection, id);
        foreach (var (sourceCollection, foreignKey) in inbound)
        {
            var refs = await repository.QueryWhereInAsync(sourceCollection, foreignKey, [typedId], ct);
            if (refs.Count > 0)
                throw new RelationConflictException(
                    $"Cannot delete '{collection}/{id}': referenced by '{sourceCollection}'.");
        }
    }

    /// <summary>
    /// The recursive purge core (DB-1/DB-2, Task 5). Runs, in order: (1) the Restrict guard — same
    /// as <see cref="CheckRestrictAsync"/>; (2) SetNull every inbound FK; (3) recursively purge every
    /// inbound Cascade row through this SAME method (<paramref name="visited"/> is a cross-recursion
    /// cycle guard: a (collection, id) pair already being purged is skipped rather than looping
    /// forever on a cyclic Cascade graph); (4) delete this item's own M2M junction rows (parent side)
    /// and any OTHER collection's M2M junction rows that target this item (target side); (5) delete
    /// its translation sidecar rows; (6) delete its revision history; (7) delete the row itself.
    /// Cascade-deleted rows go through steps 1-7 too, so their own junctions/translations/revisions
    /// are cleaned up exactly like the top-level target. Returns whether the row existed (step 7's
    /// result) — false for an id that does not exist, or one already visited in this purge.
    /// </summary>
    private async Task<bool> PurgeCoreAsync(
        string collection, string id, HashSet<(string Collection, string Id)> visited, CancellationToken ct)
    {
        if (!visited.Add((collection, id))) return false;

        var meta = Meta(collection);
        await CheckRestrictAsync(collection, id, ct);
        var typedId = TypedId(collection, id);

        foreach (var (sourceCollection, foreignKey) in graph.InboundSetNull(collection))
            await repository.SetForeignKeyNullAsync(sourceCollection, foreignKey, typedId, ct);

        foreach (var (sourceCollection, foreignKey) in graph.InboundCascade(collection))
        {
            var srcDesc = registry.Get(sourceCollection);
            if (srcDesc is null) continue;   // defensive: RelationshipGraph already validates targets are known
            var srcIdProp = srcDesc.EntityType.GetProperty(srcDesc.IdProperty);
            if (srcIdProp is null) continue;

            // Bypasses the soft-delete filter: an already-trashed row of a Cascade source collection
            // that still references the purge target must still be found and cascade-purged too.
            var referencing = await repository.QueryWhereInWithDeletedAsync(sourceCollection, foreignKey, [typedId], ct);
            foreach (var row in referencing)
            {
                var childId = srcIdProp.GetValue(row)?.ToString();
                if (childId is not null)
                    await PurgeCoreAsync(sourceCollection, childId, visited, ct);
            }
        }

        foreach (var desc in m2mSource.M2MDescriptors(collection))
            await repository.DeleteByPropertyAsync(desc.JunctionType, desc.ParentFkProperty, typedId, ct);
        foreach (var inboundDesc in m2mSource.InboundM2MDescriptors(collection))
            await repository.DeleteByPropertyAsync(
                inboundDesc.Descriptor.JunctionType, inboundDesc.Descriptor.TargetFkProperty, typedId, ct);

        if (meta.Translation is { } tm)
            await repository.DeleteByPropertyAsync(tm.TranslationEntityType, tm.ForeignKeyProperty, typedId, ct);

        if (meta.Revisions)
            await revisions.DeleteForItemAsync(collection, id, ct);

        return await repository.DeleteAsync(collection, id, ct);
    }

    /// <summary>Coerces a string id to <paramref name="collection"/>'s PK CLR type (e.g. Guid, long).</summary>
    private object TypedId(string collection, string id)
    {
        var d = registry.Get(collection) ?? throw new CollectionNotFoundException(collection);
        var pkProp = d.EntityType.GetProperty(d.IdProperty,
                         System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance)
                     ?? throw new InvalidOperationException(
                         $"Collection '{collection}' has no primary-key property '{d.IdProperty}'.");
        return IdParsing.ParseTo(id, pkProp.PropertyType);
    }

    /// <summary>
    /// Restores a soft-deleted row: looks it up ignoring the soft-delete floor (it is, by definition,
    /// trashed), clears its <c>DeletedAt</c>/<c>DeletedBy</c> marker if still set, and returns the
    /// re-read live projection. Returns null for an unknown id (404); idempotent when the row is
    /// already live.
    /// </summary>
    public async Task<IReadOnlyDictionary<string, object?>?> RestoreAsync(
        string collection, string id, CancellationToken ct = default)
    {
        var meta = Meta(collection);
        if (!permissions.CanDelete(collection)) throw new PermissionDeniedException("Delete not permitted.");
        RequireSuperAdminForAdminOnly(meta);

        // Find the row ignoring the soft-delete floor (it is, by definition, trashed).
        var entity = await repository.GetByIdAsync(collection, id, DeletedFilter.With, ct);
        if (entity is null) return null;                       // unknown id -> 404

        // Only a genuinely-trashed row is restored (and recorded): an already-live row is a no-op, so
        // it neither bumps Version nor records a spurious "restore" revision. DB-8: the restore clears
        // DeletedAt, bumps Version (repository, AuditableEntity only) and — for a revisioned collection
        // — records a "restore" revision, all in ONE transaction (capture failure rolls it all back).
        if (entity is ISoftDeletable sd && sd.DeletedAt is not null)
        {
            await repository.InTransactionAsync(async () =>
            {
                await repository.RestoreAsync(collection, id, ct);
                await CaptureRevisionAsync(collection, id, meta, "restore", ct);
            }, ct);
        }

        var restored = await repository.GetByIdAsync(collection, id, DeletedFilter.Exclude, ct);
        return restored is null ? null : Project(restored, meta, null);
    }

    /// <summary>
    /// DB-8: for a revisioned collection, re-reads the just-trashed/just-restored row (ignoring the
    /// soft-delete floor) and captures a revision under <paramref name="operation"/> ("delete" /
    /// "restore"). No-op when the collection keeps no revisions or the row vanished. Must be called
    /// inside the trash/restore transaction so the snapshot commits atomically with the state change.
    /// </summary>
    private async Task CaptureRevisionAsync(
        string collection, string id, CollectionMetadata meta, string operation, CancellationToken ct)
    {
        if (!meta.Revisions) return;
        var entity = await repository.GetByIdAsync(collection, id, DeletedFilter.With, ct);
        if (entity is null) return;
        var snapshot = await snapshotBuilder.BuildAsync(collection, entity, ct);
        await revisions.CaptureAsync(collection, id, operation, snapshot, ct);
    }

    /// <summary>
    /// Reverts an item to a past revision by re-applying that revision's snapshot as a normal update
    /// (append-only: a new "revert" revision is recorded; forward history is never deleted). Returns the
    /// re-read item, or null for an unknown collection-revision/item (→ 404). Requires write permission.
    /// </summary>
    public async Task<IReadOnlyDictionary<string, object?>?> RevertAsync(
        string collection, string id, long revisionNumber, CancellationToken ct = default)
    {
        var meta = Meta(collection);
        if (!permissions.CanWrite(collection)) throw new PermissionDeniedException("Write not permitted.");
        RequireSuperAdminForAdminOnly(meta);
        if (!meta.Revisions) return null;                                   // collection keeps no revisions -> 404

        var rec = await revisions.GetAsync(collection, id, revisionNumber, ct);
        if (rec is null) return null;                                       // unknown revision/item -> 404

        // The snapshot IS a valid update body by construction; drop `version` so revert does not echo a
        // stale optimistic-concurrency token (it would 409 against the current row). Keep the JsonDocument
        // alive across the awaited update (the body's JsonElement must stay valid).
        using var src = JsonDocument.Parse(rec.Snapshot);
        using var doc = JsonDocument.Parse(
            StripKeys(src.RootElement, new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "version" }));
        return await UpdateCoreAsync(collection, id, doc.RootElement, "revert", ct);
    }

    /// <summary>Newest-first revision metadata for an item. Requires read permission. Empty for a
    /// non-revisioned collection.</summary>
    public async Task<IReadOnlyList<Struo.Application.Revisions.RevisionInfo>> ListRevisionsAsync(
        string collection, string id, CancellationToken ct = default)
    {
        var meta = Meta(collection);
        if (!permissions.CanRead(collection)) throw new PermissionDeniedException("Read not permitted.");
        if (!meta.Revisions) return [];
        return await revisions.ListAsync(collection, id, ct);
    }

    /// <summary>A single revision incl. its snapshot. Requires read permission. Null for an
    /// unknown revision or a non-revisioned collection (→ 404).
    /// <para>
    /// (SEC-2) The snapshot <see cref="RevisionSnapshotBuilder"/> captures is the FULL item state,
    /// including <see cref="Struo.Domain.Metadata.Models.FieldMetadata.Hidden"/> fields — a revert needs
    /// every field, not just the ones the caller may read. That full-fidelity snapshot is internal to
    /// <see cref="RevertAsync"/> only (it reads the revision store directly, bypassing this method).
    /// The snapshot returned HERE — to any external caller, i.e. REST <c>GET
    /// /api/items/{collection}/{id}/revisions/{n}</c> or GraphQL <c>xRevision</c> — has hidden fields
    /// redacted via <see cref="RevisionSnapshotRedactor.RedactHidden"/> before being handed back. Access
    /// is otherwise gated only by collection-level <see cref="Struo.Application.Security.IPermissionService.CanRead"/>;
    /// a future field-level-read-grant feature should still tighten this further.
    /// </para></summary>
    public async Task<Struo.Application.Revisions.RevisionRecord?> GetRevisionAsync(
        string collection, string id, long revisionNumber, CancellationToken ct = default)
    {
        var meta = Meta(collection);
        if (!permissions.CanRead(collection)) throw new PermissionDeniedException("Read not permitted.");
        if (!meta.Revisions) return null;
        var rec = await revisions.GetAsync(collection, id, revisionNumber, ct);
        if (rec is null) return null;
        return rec with { Snapshot = RevisionSnapshotRedactor.RedactHidden(rec.Snapshot, meta) };
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

        // Json fields carry an arbitrary JSON object/array/scalar; binding that into their string
        // property would make System.Text.Json throw. Strip them here and set the raw text below.
        foreach (var jsonField in meta.Fields.Where(f => f.Interface == FieldInterface.Json))
            stripNames.Add(jsonField.Name);

        object entity;
        if (stripNames.Count > 0)
        {
            // Parse into a using-scoped document so the ArrayPool buffer is returned promptly.
            using var stripped = JsonDocument.Parse(StripKeys(body, stripNames));
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
            var pi = d.EntityType.GetProperty(prop);
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

        // Max length — CMS-layer limit (7g.5); the DB column width is SqlSugar's separate concern.
        // Runs after RichText sanitization so the stored value is what gets measured.
        foreach (var field in meta.Fields.Where(f => f.MaxLength is > 0 && !f.Translatable))
        {
            var pi = d.FieldToProperty.TryGetValue(field.Name, out var prop) ? d.EntityType.GetProperty(prop) : null;
            if (pi?.GetValue(entity) is string value && value.Length > field.MaxLength!.Value)
                throw new QueryException($"Field '{field.Name}' exceeds maximum length {field.MaxLength}.");
        }

        // Multi-value fields (MultiSelect/CheckboxGroup/Tags) live on the parent entity as
        // List<string> / List<TagItem>. Validate membership / non-blank tags, enforce Required as
        // non-empty, de-duplicate, and coerce blank tag labels to null. Non-translatable only.
        foreach (var field in meta.Fields.Where(f => MultiValueInterfaces.Contains(f.Interface) && !f.Translatable))
        {
            if (!d.FieldToProperty.TryGetValue(field.Name, out var prop)) continue;
            var pi = d.EntityType.GetProperty(prop);
            if (pi is not { CanWrite: true }) continue;

            if (field.Interface == FieldInterface.Tags)
            {
                var tags = (pi.GetValue(entity) as IEnumerable<TagItem>) ?? [];
                var seen = new HashSet<string>(StringComparer.Ordinal);
                var cleaned = new List<TagItem>();
                foreach (var t in tags)
                {
                    if (t is null || string.IsNullOrWhiteSpace(t.Value))
                        throw new QueryException($"Field '{field.Name}' has a tag with an empty value.");
                    if (!seen.Add(t.Value)) continue; // de-dup by value, keep first
                    var label = string.IsNullOrWhiteSpace(t.Label) ? null : t.Label;
                    cleaned.Add(new TagItem(t.Value, label));
                }
                if (field.Required && cleaned.Count == 0)
                    throw new QueryException($"Field '{field.Name}' is required.");
                pi.SetValue(entity, cleaned);
            }
            else // option-bound MultiSelect / CheckboxGroup
            {
                var values = (pi.GetValue(entity) as IEnumerable<string>) ?? [];
                var allowed = (field.Options ?? []).Select(o => o.Value).ToHashSet(StringComparer.Ordinal);
                var seen = new HashSet<string>(StringComparer.Ordinal);
                var cleaned = new List<string>();
                foreach (var v in values)
                {
                    if (v is null) continue;
                    if (!allowed.Contains(v))
                        throw new QueryException($"Field '{field.Name}' has value '{v}' not in its options.");
                    if (seen.Add(v)) cleaned.Add(v); // de-dup, keep first
                }
                if (field.Required && cleaned.Count == 0)
                    throw new QueryException($"Field '{field.Name}' is required.");
                pi.SetValue(entity, cleaned);
            }
        }

        // KeyValue fields (Dictionary<string,string>) live on the parent entity. Reject blank keys and
        // enforce Required as a non-empty map. Values may be empty; duplicate keys are impossible
        // (System.Text.Json last-wins bind). Non-translatable only.
        foreach (var field in meta.Fields.Where(f => f.Interface == FieldInterface.KeyValue && !f.Translatable))
        {
            if (!d.FieldToProperty.TryGetValue(field.Name, out var prop)) continue;
            var pi = d.EntityType.GetProperty(prop);
            if (pi is not { CanWrite: true }) continue;

            var map = pi.GetValue(entity) as IDictionary<string, string>;
            if (map is not null)
            {
                foreach (var key in map.Keys)
                    if (string.IsNullOrWhiteSpace(key))
                        throw new QueryException($"Field '{field.Name}' has an entry with an empty key.");
            }
            if (field.Required && (map is null || map.Count == 0))
                throw new QueryException($"Field '{field.Name}' is required.");
        }

        // Files fields (List<Guid>) live on the parent entity as an ordered list of file ids. Drop
        // Guid.Empty, de-duplicate keeping first (preserves order), and enforce Required as a non-empty
        // list. No existence check — mirrors the scalar File/Image field (a deleted file degrades to a
        // raw-id fallback in the picker). A non-guid array element already 400s at the deserialize
        // guard. Non-translatable only.
        foreach (var field in meta.Fields.Where(f => f.Interface == FieldInterface.Files && !f.Translatable))
        {
            if (!d.FieldToProperty.TryGetValue(field.Name, out var prop)) continue;
            var pi = d.EntityType.GetProperty(prop);
            if (pi is not { CanWrite: true }) continue;

            var ids = (pi.GetValue(entity) as IEnumerable<Guid>) ?? [];
            var seen = new HashSet<Guid>();
            var cleaned = new List<Guid>();
            foreach (var id in ids)
            {
                if (id == Guid.Empty) continue;
                if (seen.Add(id)) cleaned.Add(id); // de-dup, keep first (preserves order)
            }
            if (field.Required && cleaned.Count == 0)
                throw new QueryException($"Field '{field.Name}' is required.");
            pi.SetValue(entity, cleaned);
        }

        // Repeater fields (List<TChild>) live on the parent entity as an ordered list of typed child
        // objects. Drop fully-blank rows (every sub-field null or whitespace string), enforce each
        // kept row's sub-field Required / Select-Radio option membership / MaxLength, and enforce the
        // parent Required as a non-empty list. Non-translatable only. A wrong-shaped element already
        // 400s at the deserialize guard.
        foreach (var field in meta.Fields.Where(f => f.Interface == FieldInterface.Repeater && !f.Translatable))
        {
            if (field.Fields is null) continue;
            if (!d.FieldToProperty.TryGetValue(field.Name, out var prop)) continue;
            var pi = d.EntityType.GetProperty(prop);
            if (pi is not { CanWrite: true }) continue;
            if (pi.GetValue(entity) is not System.Collections.IEnumerable rowsEnum) continue;

            var cleaned = (System.Collections.IList)Activator.CreateInstance(pi.PropertyType)!;

            var rowIndex = 0;
            foreach (var row in rowsEnum)
            {
                rowIndex++;
                if (row is null) continue;

                // Blank-row detection: every sub-field is null or a whitespace-only string.
                var allBlank = true;
                foreach (var sub in field.Fields)
                {
                    var v = ReadProp(row, sub.Name);
                    if (v is null) continue;
                    if (v is string sv && string.IsNullOrWhiteSpace(sv)) continue;
                    allBlank = false; break;
                }
                if (allBlank) continue;

                foreach (var sub in field.Fields)
                {
                    var v = ReadProp(row, sub.Name);
                    var sv = v as string;

                    if (sub.Required && (v is null || (sv is not null && string.IsNullOrWhiteSpace(sv))))
                        throw new QueryException(
                            $"Repeater field '{field.Name}' row {rowIndex}: '{sub.Name}' is required.");

                    if (sv is not null && sub.MaxLength is > 0 && sv.Length > sub.MaxLength.Value)
                        throw new QueryException(
                            $"Repeater field '{field.Name}' row {rowIndex}: '{sub.Name}' exceeds maximum length {sub.MaxLength}.");

                    if (sv is not null && sub.Options is { Count: > 0 } &&
                        !string.IsNullOrEmpty(sv) &&
                        !sub.Options.Any(o => string.Equals(o.Value, sv, StringComparison.Ordinal)))
                        throw new QueryException(
                            $"Repeater field '{field.Name}' row {rowIndex}: '{sub.Name}' value '{sv}' is not in its options.");
                }
                cleaned.Add(row);
            }

            if (field.Required && cleaned.Count == 0)
                throw new QueryException($"Field '{field.Name}' is required.");
            pi.SetValue(entity, cleaned);
        }
        return entity;
    }

    private IReadOnlyDictionary<string, object?> Project(object entity, CollectionMetadata meta, IReadOnlyList<string>? fields)
    {
        var d = registry.Get(meta.Name)!;
        var dict = new Dictionary<string, object?>();

        const string idKey = "id";
        // d.IdProperty is an exact CLR property name; d.Properties is keyed OrdinalIgnoreCase, so this
        // resolves the same PropertyInfo the old case-sensitive GetProperty(d.IdProperty) returned.
        dict[idKey] = d.Properties.GetValueOrDefault(d.IdProperty)?.GetValue(entity);

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
}
