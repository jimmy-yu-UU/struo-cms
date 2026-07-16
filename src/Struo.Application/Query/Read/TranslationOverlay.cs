// src/Struo.Application/Query/Read/TranslationOverlay.cs
using Struo.Application.Metadata;
using Struo.Domain.Metadata.Enums;
using Struo.Domain.Metadata.Models;

namespace Struo.Application.Query;

/// <summary>
/// When <c>meta</c> declares a translation sidecar, batch-loads the translation
/// rows for all parent ids (optionally filtered to <c>locale</c>), groups them by
/// the parent FK, and attaches a <c>translations</c> map
/// (<c>{ locale: { camelField: value } }</c>) onto each projected row.
/// </summary>
public sealed class TranslationOverlay(IItemRepository repository, IEntityRegistry registry, ItemProjector projector)
{
    /// <inheritdoc cref="TranslationOverlay"/>
    public async Task ApplyAsync(
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
            .Select(e => PropertyAccessorCache.Read(e, idProp))
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
            var fk = PropertyAccessorCache.Read(tr, tm.ForeignKeyProperty);
            var loc = PropertyAccessorCache.Read(tr, tm.LocaleProperty) as string;
            if (fk is null || loc is null) continue;
            var key = fk.ToString()!;
            if (!grouped.TryGetValue(key, out var byLocale))
                grouped[key] = byLocale = new Dictionary<string, Dictionary<string, object?>>();

            var fieldMap = new Dictionary<string, object?>();
            foreach (var (camel, clr) in camelToClr)
                fieldMap[camel] = PropertyAccessorCache.Read(tr, clr);
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
                    var projected = projector.ProjectFor("file", f, null);
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
            var pid = PropertyAccessorCache.Read(entities[i], idProp)?.ToString();
            var dict = (Dictionary<string, object?>)rows[i];
            dict["translations"] = pid is not null && grouped.TryGetValue(pid, out var byLocale)
                ? byLocale
                : new Dictionary<string, Dictionary<string, object?>>();
        }
    }
}
