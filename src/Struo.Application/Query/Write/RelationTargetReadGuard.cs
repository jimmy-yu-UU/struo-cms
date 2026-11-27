// src/Struo.Application/Query/Write/RelationTargetReadGuard.cs
using System.Text.Json;
using Struo.Application.Files;
using Struo.Application.Metadata;
using Struo.Application.Security;
using Struo.Domain.Metadata.Enums;
using Struo.Domain.Metadata.Models;
using Struo.Domain.Query;

namespace Struo.Application.Query;

/// <summary>A key of a write body that links another collection: the collection it links and the key's field name.</summary>
internal readonly record struct SentLink(string Target, string Key, bool InTranslations);

/// <summary>
/// A write whose body carries a key that links another collection needs read on that collection, as the read
/// side needs it to expand, filter or sort through the relation. The keys are a many-to-many array (empty
/// included), a many-to-one foreign key (null included), a <c>File</c>/<c>Image</c>/<c>Files</c> field, which
/// links the <c>file</c> collection (a translated one is read from the <c>translations</c> object of any locale),
/// and the two keys of a junction collection, which link the collection each one points at. A key the body
/// omits needs nothing, and neither does a key whose field is system or <c>ReadOnly</c> (an audit key such as
/// <c>createdBy</c>, a <c>ReadOnly</c> foreign key): the binder drops it, so it links nothing. The check runs
/// before any id is parsed or looked up, so the answer is the same for an existing, a missing and a malformed id.
/// <para>
/// An update whose file field carries the value the item already stores needs no read on <c>file</c>: the item
/// read returns that id to every reader of the collection (<see cref="RequireFilesAsync"/>). The same holds
/// for a junction key sent with the stored value (<see cref="RequireJunctionKeys"/>).
/// </para>
/// Why: docs/ai/decisions/a-link-write-needs-read-on-the-target.md
/// </summary>
public sealed class RelationTargetReadGuard(IM2MDescriptorSource m2mSource, IPermissionService permissions)
{
    private const string TranslationsKey = "translations";

    /// <summary>The refusal the read side and this guard give for a collection the caller cannot read.</summary>
    public static PermissionDeniedException ReadDenied(string collection) =>
        new($"Read not permitted on '{collection}'.");

    /// <summary>
    /// Throws <see cref="PermissionDeniedException"/> for the first sent link whose target the caller cannot read:
    /// the many-to-many, many-to-one and junction keys first, then the file fields. A create has no exemption.
    /// </summary>
    public void Require(CollectionMetadata meta, JsonElement body)
    {
        RequireLinks(meta, body);
        if (SentFiles(meta, body).Any() && !permissions.CanRead(FileCollection.Name))
            throw ReadDenied(FileCollection.Name);
    }

    /// <summary>The many-to-many, many-to-one and junction keys of <see cref="Require"/>, without the file fields.</summary>
    internal void RequireLinks(CollectionMetadata meta, JsonElement body) =>
        RequireReadable(SentLinks(meta, body), body);

    /// <summary>
    /// The many-to-many and many-to-one keys of an update. Its junction keys are checked by
    /// <see cref="RequireJunctionKeys"/> once the stored row is loaded.
    /// </summary>
    internal void RequireRelationLinks(CollectionMetadata meta, JsonElement body)
    {
        var sent = WritableNames(meta, body);
        RequireReadable(ManyToManyLinks(meta, body).Concat(ManyToOneLinks(meta, sent)), body);
    }

    /// <summary>
    /// The junction keys of an update. A caller who cannot read a key's target may send the key back with the
    /// value <paramref name="existing"/> stores (nothing new is linked); a <c>Hidden</c> key is never exempt, as the
    /// item read does not return it. Any other value is refused like on a create.
    /// </summary>
    internal void RequireJunctionKeys(CollectionMetadata meta, EntityDescriptor d, JsonElement body, object existing)
    {
        if (body.ValueKind != JsonValueKind.Object) return;
        var refused = JunctionLinks(meta, WritableNames(meta, body))
            .Where(link => !permissions.CanRead(link.Target) && !HoldsStoredKey(meta, d, body, existing, link.Key))
            .Select(link => link.Target)
            .FirstOrDefault();
        if (refused is not null) throw ReadDenied(refused);
    }

    private void RequireReadable(IEnumerable<SentLink> links, JsonElement body)
    {
        if (body.ValueKind != JsonValueKind.Object) return;
        var refused = links.Where(link => !permissions.CanRead(link.Target)).Select(link => link.Target).FirstOrDefault();
        if (refused is not null) throw ReadDenied(refused);
    }

    private static bool HoldsStoredKey(
        CollectionMetadata meta, EntityDescriptor d, JsonElement body, object existing, string property)
    {
        var name = LinkExistenceCheck.FieldNameOf(d, property);
        if (meta.Fields.Any(f => f.Hidden && string.Equals(f.Name, name, StringComparison.OrdinalIgnoreCase)))
            return false;
        // The binder takes any spelling of the name and the last one wins, so every spelling must hold the stored value.
        var spellings = body.EnumerateObject()
            .Where(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase))
            .Select(p => p.Value)
            .ToList();
        var stored = d.Properties.GetValueOrDefault(property)?.GetValue(existing);
        return spellings.Count > 0 && spellings.All(sent => SameKey(stored, sent));
    }

    // Equal as stored: a Guid by value, a string exactly, a number as written.
    private static bool SameKey(object? stored, JsonElement sent) => stored switch
    {
        null => sent.ValueKind == JsonValueKind.Null,
        Guid id => sent.ValueKind == JsonValueKind.String && Guid.TryParse(sent.GetString(), out var parsed) && parsed == id,
        string text => sent.ValueKind == JsonValueKind.String
                       && string.Equals(text, sent.GetString(), StringComparison.Ordinal),
        _ => sent.ValueKind == JsonValueKind.Number
             && sent.GetRawText() == Convert.ToString(stored, System.Globalization.CultureInfo.InvariantCulture),
    };

    /// <summary>
    /// The file fields of an update. A caller who cannot read <c>file</c> may send a field back only with the
    /// value <paramref name="loadStored"/> reports for it (the same id, or the same ids in the same order);
    /// any other value is refused like on a create. <paramref name="loadStored"/> runs only when a refusal is possible.
    /// </summary>
    internal async Task RequireFilesAsync(CollectionMetadata meta, JsonElement body, Func<Task<StoredFileLinks>> loadStored)
    {
        if (body.ValueKind != JsonValueKind.Object || permissions.CanRead(FileCollection.Name)) return;
        var sent = SentFiles(meta, body).ToList();
        if (sent.Count == 0) return;
        // A Hidden field is not returned by the item read, so no stored value may be matched against it.
        if (sent.Any(file => file.Field.Hidden)) throw ReadDenied(FileCollection.Name);
        var stored = await loadStored();
        if (sent.Any(file => !stored.Holds(file))) throw ReadDenied(FileCollection.Name);
    }

    /// <summary>
    /// The keys of <paramref name="body"/> that <see cref="Require"/> would refuse for this caller, in the order it
    /// meets them. A revert removes them from the snapshot it replays.
    /// </summary>
    internal IReadOnlyList<SentLink> UnreadableKeys(CollectionMetadata meta, JsonElement body) =>
        body.ValueKind != JsonValueKind.Object
            ? []
            : SentLinks(meta, body)
                .Concat(SentFiles(meta, body).Select(file =>
                    new SentLink(FileCollection.Name, file.Field.Name, file.Locale is not null)))
                .Where(link => !permissions.CanRead(link.Target))
                .ToList();

    private IEnumerable<SentLink> SentLinks(CollectionMetadata meta, JsonElement body)
    {
        var sent = WritableNames(meta, body);
        return ManyToManyLinks(meta, body)
            .Concat(ManyToOneLinks(meta, sent))
            .Concat(JunctionLinks(meta, sent));
    }

    // The body keys a write can set: a system or ReadOnly field is dropped by the binder, a foreign key
    // included, so naming one links nothing and needs no read on its target.
    private static HashSet<string> WritableNames(CollectionMetadata meta, JsonElement body)
    {
        var names = ItemDeserializer.PresentNames(body);
        names.ExceptWith(ItemDeserializer.ServerOwnedFieldNames(meta));
        return names;
    }

    // A many-to-many key is matched by its exact name and a foreign key ignoring case: each mirrors how its
    // consumer reads it (the relation sync looks the array up by name, the deserializer binds keys ignoring case).
    private IEnumerable<SentLink> ManyToManyLinks(CollectionMetadata meta, JsonElement body) =>
        m2mSource.M2MDescriptors(meta.Name)
            .Where(desc => JsonBodyUtil.TryGetRelationArray(body, desc.RelationName, out _))
            .Select(desc => new SentLink(desc.TargetCollection, desc.RelationName, false));

    private static IEnumerable<SentLink> ManyToOneLinks(CollectionMetadata meta, HashSet<string> sent) =>
        meta.Relations
            .Where(rel => rel is { Kind: RelationKind.ManyToOne, ForeignKey: { } key } && sent.Contains(key))
            .Select(rel => new SentLink(rel.TargetCollection, rel.ForeignKey!, false));

    // The keys of a junction collection are plain fields, not relations; each links the collection it points at.
    private IEnumerable<SentLink> JunctionLinks(CollectionMetadata meta, HashSet<string> sent)
    {
        foreach (var link in m2mSource.JunctionKeyLinks(meta.Name))
        {
            if (sent.Contains(link.ParentKeyProperty))
                yield return new SentLink(link.ParentCollection, link.ParentKeyProperty, false);
            if (sent.Contains(link.TargetKeyProperty))
                yield return new SentLink(link.TargetCollection, link.TargetKeyProperty, false);
        }
    }

    // A file field the body carries, under every spelling the binder accepts: the root ones are bound ignoring
    // case, the translated ones are addressed by name ignoring case inside the locale objects.
    private static IEnumerable<SentFile> SentFiles(CollectionMetadata meta, JsonElement body)
    {
        if (body.ValueKind != JsonValueKind.Object) yield break;
        var root = FieldsByName(FileFields.Root(meta));
        var translated = FieldsByName(FileFields.Translated(meta));
        foreach (var prop in body.EnumerateObject().Where(p => root.ContainsKey(p.Name)))
            yield return new SentFile(root[prop.Name], null, prop.Value);

        if (translated.Count == 0 || !body.TryGetProperty(TranslationsKey, out var locales)
            || locales.ValueKind != JsonValueKind.Object)
            yield break;
        foreach (var locale in locales.EnumerateObject().Where(l => l.Value.ValueKind == JsonValueKind.Object))
        {
            foreach (var prop in locale.Value.EnumerateObject().Where(p => translated.ContainsKey(p.Name)))
                yield return new SentFile(translated[prop.Name], locale.Name, prop.Value);
        }
    }

    private static Dictionary<string, FieldMetadata> FieldsByName(IEnumerable<FieldMetadata> fields) =>
        fields.ToDictionary(field => field.Name, StringComparer.OrdinalIgnoreCase);
}

/// <summary>A file field a body sends, with its locale when it sits in <c>translations</c>.</summary>
internal readonly record struct SentFile(FieldMetadata Field, string? Locale, JsonElement Value);
