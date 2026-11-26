// src/Struo.Application/Query/Write/LinkExistenceCheck.cs
using System.Text.Json;
using Struo.Application.Files;
using Struo.Application.Metadata;
using Struo.Application.Security;
using Struo.Domain.Metadata.Enums;
using Struo.Domain.Metadata.Models;
using Struo.Domain.Query;

namespace Struo.Application.Query;

/// <summary>The ids a write newly assigns to one key, and the collection they must exist in.</summary>
internal sealed record PendingLink(string Key, string Target, IReadOnlyList<object> Ids, bool IsList);

/// <summary>
/// A key of a write that links another collection must name an existing row of it: a many-to-one foreign key,
/// a <c>File</c>/<c>Image</c>/<c>Files</c> field (translated ones included) and the two keys of a junction
/// collection (many-to-many arrays are checked by <see cref="ItemWriteSideSync"/>). Only an id the write newly
/// assigns is checked: on an update an id equal to the stored one passes, so an item that already holds a
/// dangling or trashed reference stays saveable when the client sends the field back. A trashed row is not a
/// valid new assignment, except for a revert. The ids of one target collection are looked up with one query.
/// The <c>permission</c> collection's <c>roleId</c> has rules of its own and is not covered.
/// </summary>
internal sealed class LinkExistenceCheck(
    IItemRepository repository, IEntityRegistry registry, IM2MDescriptorSource m2mSource)
{
    private const string TranslationsKey = "translations";

    private readonly record struct Slot(string Key, string Target, string Property, bool IsList);

    /// <summary>
    /// The ids the root keys <paramref name="sent"/> newly assign. <paramref name="stored"/> is the row before the
    /// write (null on a create) and <paramref name="incoming"/> the entity bound from the body.
    /// </summary>
    internal IReadOnlyList<PendingLink> RootLinks(
        CollectionMetadata meta, EntityDescriptor d, IReadOnlySet<string> sent, object? stored, object incoming)
    {
        var links = new List<PendingLink>();
        foreach (var slot in Slots(meta, d).Where(slot => sent.Contains(slot.Key)))
        {
            var storedIds = stored is null ? [] : IdsOf(Read(d, slot.Property, stored));
            var added = IdsOf(Read(d, slot.Property, incoming)).Where(id => !storedIds.Contains(id)).ToList();
            if (added.Count > 0) links.Add(new PendingLink(slot.Key, slot.Target, added, slot.IsList));
        }
        return links;
    }

    /// <summary>
    /// Refuses (400) a new assignment to a row that does not exist, among <paramref name="rootLinks"/> and the
    /// translated file fields <paramref name="body"/> sends. <paramref name="parentId"/> is null on a create.
    /// </summary>
    internal async Task EnsureAsync(
        CollectionMetadata meta, JsonElement body, IReadOnlyList<PendingLink> rootLinks, object? parentId,
        bool includeDeleted, CancellationToken ct)
    {
        var links = rootLinks.Concat(await TranslatedLinksAsync(meta, body, parentId, ct)).ToList();
        foreach (var target in links.GroupBy(link => link.Target, StringComparer.OrdinalIgnoreCase))
        {
            var found = await ExistingIdsAsync(target.Key, target.SelectMany(l => l.Ids).Distinct().ToList(), includeDeleted, ct);
            var missing = target.FirstOrDefault(link => link.Ids.Any(id => !found.Contains(id.ToString()!)));
            if (missing is not null) throw Missing(missing);
        }
    }

    private static QueryException Missing(PendingLink link) => new(link.IsList
        ? $"One or more ids in '{link.Key}' do not exist in '{link.Target}'."
        : $"The id in '{link.Key}' does not exist in '{link.Target}'.");

    private async Task<HashSet<string>> ExistingIdsAsync(
        string target, List<object> ids, bool includeDeleted, CancellationToken ct)
    {
        var rows = includeDeleted
            ? await repository.QueryWhereInWithDeletedAsync(target, "id", ids, ct)
            : await repository.QueryWhereInAsync(target, "id", ids, ct);
        var d = registry.Get(target)!;
        return rows.Select(row => Read(d, d.IdProperty, row)?.ToString()).OfType<string>().ToHashSet();
    }

    // The keys that link another collection, each once (a key can be both a relation and a junction field).
    private IEnumerable<Slot> Slots(CollectionMetadata meta, EntityDescriptor d)
    {
        var slots = new Dictionary<string, Slot>(StringComparer.OrdinalIgnoreCase);
        foreach (var slot in ManyToOneSlots(meta).Concat(JunctionSlots(meta, d)).Concat(FileSlots(meta, d)))
            slots.TryAdd(slot.Key, slot);
        return slots.Values;
    }

    private static IEnumerable<Slot> ManyToOneSlots(CollectionMetadata meta) =>
        string.Equals(meta.Name, RbacCollections.Permission, StringComparison.OrdinalIgnoreCase)
            ? []
            : meta.Relations
                .Where(rel => rel is { Kind: RelationKind.ManyToOne, ForeignKey: not null })
                .Select(rel => new Slot(rel.ForeignKey!, rel.TargetCollection, rel.ForeignKey!, false));

    private IEnumerable<Slot> JunctionSlots(CollectionMetadata meta, EntityDescriptor d) =>
        m2mSource.JunctionKeyLinks(meta.Name).SelectMany(link => new[]
        {
            new Slot(FieldNameOf(d, link.ParentKeyProperty), link.ParentCollection, link.ParentKeyProperty, false),
            new Slot(FieldNameOf(d, link.TargetKeyProperty), link.TargetCollection, link.TargetKeyProperty, false),
        });

    // The field name a body and the error messages use for a CLR property.
    private static string FieldNameOf(EntityDescriptor d, string property) =>
        d.FieldToProperty.FirstOrDefault(kv => string.Equals(kv.Value, property, StringComparison.OrdinalIgnoreCase)).Key
        ?? property;

    private static IEnumerable<Slot> FileSlots(CollectionMetadata meta, EntityDescriptor d) =>
        meta.Fields
            .Where(f => IsFileField(f) && !f.Translatable && !f.IsSystem && !f.ReadOnly)
            .Where(f => d.FieldToProperty.ContainsKey(f.Name))
            .Select(f => new Slot(f.Name, FileCollection.Name, d.FieldToProperty[f.Name], f.Interface == FieldInterface.Files));

    // The ids a translated file field newly assigns, per locale object of the body.
    private async Task<IReadOnlyList<PendingLink>> TranslatedLinksAsync(
        CollectionMetadata meta, JsonElement body, object? parentId, CancellationToken ct)
    {
        var fields = meta.Fields.Where(f => IsFileField(f) && f.Translatable)
            .ToDictionary(f => f.Name, StringComparer.OrdinalIgnoreCase);
        if (meta.Translation is not { } tm || fields.Count == 0 || body.ValueKind != JsonValueKind.Object
            || !body.TryGetProperty(TranslationsKey, out var locales) || locales.ValueKind != JsonValueKind.Object)
            return [];

        var sent = SentTranslated(locales, fields);
        if (sent.Count == 0) return [];
        var stored = parentId is null ? [] : await StoredAsync(tm, parentId, ct);
        return sent
            .Select(s => (s.Field, Ids: s.Ids.Where(id => !stored.Contains((s.Locale, s.Field.Name.ToLowerInvariant(), id))).ToList()))
            .Where(s => s.Ids.Count > 0)
            .GroupBy(s => s.Field.Name, StringComparer.OrdinalIgnoreCase)
            .Select(g => new PendingLink(
                g.First().Field.Name, FileCollection.Name, g.SelectMany(s => s.Ids).Distinct().Cast<object>().ToList(),
                false))
            .ToList();
    }

    private static List<(string Locale, FieldMetadata Field, List<Guid> Ids)> SentTranslated(
        JsonElement locales, Dictionary<string, FieldMetadata> fields)
    {
        var sent = new List<(string, FieldMetadata, List<Guid>)>();
        foreach (var locale in locales.EnumerateObject().Where(l => l.Value.ValueKind == JsonValueKind.Object))
        {
            foreach (var prop in locale.Value.EnumerateObject().Where(p => fields.ContainsKey(p.Name)))
            {
                var ids = prop.Value.ValueKind == JsonValueKind.String && Guid.TryParse(prop.Value.GetString(), out var id)
                          && id != Guid.Empty
                    ? new List<Guid> { id }
                    : [];
                if (ids.Count > 0) sent.Add((locale.Name.ToUpperInvariant(), fields[prop.Name], ids));
            }
        }
        return sent;
    }

    private async Task<HashSet<(string Locale, string Field, Guid Id)>> StoredAsync(
        TranslationMetadata tm, object parentId, CancellationToken ct)
    {
        var rows = await repository.LoadTranslationsAsync(
            tm.TranslationEntityType, tm.ForeignKeyProperty, tm.LocaleProperty, [parentId], null, ct);
        var stored = new HashSet<(string, string, Guid)>();
        foreach (var row in rows)
        {
            if (PropertyAccessorCache.Read(row, tm.LocaleProperty) is not string locale) continue;
            foreach (var field in tm.Fields)
            {
                if (PropertyAccessorCache.Read(row, field) is Guid id)
                    stored.Add((locale.ToUpperInvariant(), field.ToLowerInvariant(), id));
            }
        }
        return stored;
    }

    private static object? Read(EntityDescriptor d, string property, object row) =>
        d.Properties.GetValueOrDefault(property)?.GetValue(row);

    // The ids a value holds: none for null or an empty Guid, the elements of a list, else the value itself.
    private static List<object> IdsOf(object? value) => value switch
    {
        null => [],
        Guid guid => guid == Guid.Empty ? [] : [guid],
        System.Collections.IEnumerable list and not string => list.Cast<object>().Where(id => id is not Guid g || g != Guid.Empty).ToList(),
        _ => [value],
    };

    private static bool IsFileField(FieldMetadata field) =>
        field.Interface is FieldInterface.File or FieldInterface.Image or FieldInterface.Files;
}
