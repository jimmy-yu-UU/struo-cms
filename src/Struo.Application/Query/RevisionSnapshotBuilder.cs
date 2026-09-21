// src/Struo.Application/Query/RevisionSnapshotBuilder.cs
using System.Text.Json;
using Struo.Application.Metadata;
using Struo.Domain.Metadata.Enums;
using Struo.Domain.Metadata.Models;
using Struo.Domain.Query;

namespace Struo.Application.Query;

/// <summary>
/// Assembles a canonical, revert-capable JSON snapshot of an item's post-write state. Unlike
/// the read projection (<c>ItemService.Project</c>), it (a) applies NO RBAC field filtering — a snapshot
/// must capture the whole item regardless of the writer's field grants — and (b) includes M2O foreign-key
/// ids and M2M relations (bare-id arrays, or objects carrying junction payload), so the result is
/// exactly the shape <c>ItemService.UpdateAsync</c>
/// consumes and revert round-trips through the normal write path.
/// <para>
/// The omission of RBAC/hidden-field filtering here is deliberate: a revert must be able to restore
/// every field, so <see cref="ItemService.RevertAsync"/> reads this full-fidelity snapshot straight
/// from the revision store. That full snapshot is internal-to-revert only — any snapshot
/// returned to an external caller instead goes through <see cref="ItemService.GetRevisionAsync"/>,
/// which redacts hidden fields via <see cref="RevisionSnapshotRedactor.RedactHidden"/> before handing
/// it back, so a snapshot never leaks a hidden field via REST/GraphQL.
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

        var idValue = d.Properties.GetValueOrDefault(d.IdProperty)?.GetValue(entity);
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
            var value = d.Properties.GetValueOrDefault(prop)?.GetValue(entity);
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
            // Properties is OrdinalIgnoreCase-keyed, so rel.ForeignKey (an exact CLR name) resolves
            // the same PropertyInfo a case-insensitive GetProperty binding would.
            snap[rel.ForeignKey] = d.Properties.GetValueOrDefault(rel.ForeignKey)?.GetValue(entity);
        }

        // (3) M2M relations under the relation name (e.g. "tags"). A plain relation snapshots as an
        //     ordered bare-id array (["<id>", ...]); a payload-bearing relation snapshots as ordered
        //     objects ({ "id": <targetId>, ...payload }), including Hidden payload fields — the stored
        //     snapshot must be full-fidelity so RevertAsync can restore them; redaction happens only on
        //     the external read path (see RevisionSnapshotRedactor).
        foreach (var desc in m2mSource.M2MDescriptors(collection))
        {
            var junctions = await repository.QueryEntityWhereInAsync(
                desc.JunctionType, desc.ParentFkProperty, [idValue!], ct);
            var ordered = desc.SortProperty is null
                ? junctions
                : junctions.OrderBy(j => ReadProp(j, desc.SortProperty)).ToList();
            snap[desc.RelationName] = desc.HasPayload
                ? ordered.Select(j =>
                  {
                      var o = new Dictionary<string, object?>(StringComparer.Ordinal) { ["id"] = ReadProp(j, desc.TargetFkProperty) };
                      foreach (var f in desc.JunctionPayload!) o[f.Name] = ReadProp(j, f.Property);
                      return (object?)o;
                  }).ToList()
                : ordered.Select(j => ReadProp(j, desc.TargetFkProperty)).ToList();
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
                f => PropertyAccessorCache.Resolve(tm.TranslationEntityType, f)?.Name ?? f,
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
        PropertyAccessorCache.Read(o, name);
}
