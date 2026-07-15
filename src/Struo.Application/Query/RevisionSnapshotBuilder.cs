// src/Struo.Application/Query/RevisionSnapshotBuilder.cs
using System.Reflection;
using System.Text.Json;
using Struo.Application.Metadata;
using Struo.Domain.Metadata.Enums;
using Struo.Domain.Metadata.Models;
using Struo.Domain.Query;

namespace Struo.Application.Query;

/// <summary>
/// Assembles a canonical, revert-capable JSON snapshot of an item's post-write state (Phase 9c). Unlike
/// the read projection (<c>ItemService.Project</c>), it (a) applies NO RBAC field filtering — a snapshot
/// must capture the whole item regardless of the writer's field grants — and (b) includes M2O foreign-key
/// ids and M2M relation id arrays, so the result is exactly the shape <c>ItemService.UpdateAsync</c>
/// consumes and revert round-trips through the normal write path.
/// <para>
/// The omission of RBAC/hidden-field filtering here is deliberate (see <see cref="ItemService.GetRevisionAsync"/>):
/// a revert must be able to restore every field, so gating stays at the collection-read level only.
/// </para>
/// </summary>
public sealed class RevisionSnapshotBuilder(
    IItemRepository repository,
    IMetadataProvider metadata,
    IEntityRegistry registry,
    IM2MDescriptorSource m2mSource)
{
    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

    public async Task<string> BuildAsync(string collection, object entity, CancellationToken ct = default)
    {
        var meta = metadata.GetCollection(collection)
                   ?? throw new CollectionNotFoundException(collection);
        var d = registry.Get(collection)!;
        var snap = new Dictionary<string, object?>();

        var idValue = d.EntityType.GetProperty(d.IdProperty)?.GetValue(entity);
        snap["id"] = idValue;
        if (entity is Struo.Domain.Auditing.AuditableEntity versioned)
            snap["version"] = versioned.Version;

        // (1) All [CmsField] own-field values — no RBAC filtering, no hidden-skip. Json stays raw text
        //     (parsed to a structured JsonElement so it serialises as JSON, not a quoted string).
        foreach (var field in meta.Fields)
        {
            if (field.IsSystem) continue;                 // audit fields are server-managed, not part of a revert body
            if (field.Translatable) continue;             // translatable own-fields live in `translations` below
            if (!d.FieldToProperty.TryGetValue(field.Name, out var prop)) continue;
            var value = d.EntityType.GetProperty(prop)?.GetValue(entity);
            if (field.Interface == FieldInterface.Json && value is string rawJson)
            {
                try { value = JsonSerializer.Deserialize<JsonElement>(rawJson); }
                catch (JsonException) { /* leave raw string; defensive, unreachable via the write path */ }
            }
            snap[field.Name] = value;
        }

        // (2) M2O foreign-key ids (declared via [CmsRelation], so absent from meta.Fields) under the
        //     camel FK name UpdateAsync overlays (e.g. "categoryId").
        foreach (var rel in meta.Relations)
        {
            if (rel.Kind != RelationKind.ManyToOne || rel.ForeignKey is null) continue;
            var pi = d.EntityType.GetProperty(rel.ForeignKey,
                BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
            snap[rel.ForeignKey] = pi?.GetValue(entity);
        }

        // (3) M2M relations as ordered id arrays under the relation name (e.g. "tags": ["<id>", ...]).
        foreach (var desc in m2mSource.M2MDescriptors(collection))
        {
            var junctions = await repository.QueryEntityWhereInAsync(
                desc.JunctionType, desc.ParentFkProperty, [idValue!], ct);
            var ordered = desc.SortProperty is null
                ? junctions
                : junctions.OrderBy(j => ReadProp(j, desc.SortProperty)).ToList();
            snap[desc.RelationName] = ordered
                .Select(j => ReadProp(j, desc.TargetFkProperty))
                .ToList();
        }

        // (4) All-locale translations: { locale: { camelField: rawValue } }. Image/file fields stay
        //     raw ids (they round-trip through the write path). Verbatim locale keys.
        var tm = meta.Translation;
        if (tm is not null && idValue is not null)
        {
            var tRows = await repository.LoadTranslationsAsync(
                tm.TranslationEntityType, tm.ForeignKeyProperty, tm.LocaleProperty, [idValue], null, ct);
            var camelToClr = tm.Fields.ToDictionary(
                f => f,
                f => tm.TranslationEntityType.GetProperty(f,
                        BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase)?.Name ?? f,
                StringComparer.OrdinalIgnoreCase);

            var byLocale = new Dictionary<string, object?>(StringComparer.Ordinal);
            foreach (var tr in tRows)
            {
                if (ReadProp(tr, tm.LocaleProperty) is not string loc) continue;
                var fieldMap = new Dictionary<string, object?>(StringComparer.Ordinal);
                foreach (var (camel, clr) in camelToClr) fieldMap[camel] = ReadProp(tr, clr);
                byLocale[loc] = fieldMap;
            }
            snap["translations"] = byLocale;
        }

        return JsonSerializer.Serialize(snap, JsonOpts);
    }

    private static object? ReadProp(object o, string name) =>
        o.GetType().GetProperty(name,
            BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase)?.GetValue(o);
}
