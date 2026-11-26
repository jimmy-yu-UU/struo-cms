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
/// omits needs nothing. The check runs before any id is parsed or looked up, so the answer is the same for an
/// existing, a missing and a malformed id.
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

    private void RequireLinks(CollectionMetadata meta, JsonElement body)
    {
        if (body.ValueKind != JsonValueKind.Object) return;
        foreach (var link in SentLinks(meta, body))
        {
            if (!permissions.CanRead(link.Target)) throw ReadDenied(link.Target);
        }
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
        var sent = ItemDeserializer.PresentNames(body);
        return ManyToManyLinks(meta, body)
            .Concat(ManyToOneLinks(meta, sent))
            .Concat(JunctionLinks(meta, sent));
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
        var fileFields = meta.Fields.Where(IsFileField).ToList();
        var root = FieldsByName(fileFields.Where(f => !f.Translatable && !f.IsSystem && !f.ReadOnly));
        var translated = FieldsByName(fileFields.Where(f => f.Translatable));
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

    private static bool IsFileField(FieldMetadata field) =>
        field.Interface is FieldInterface.File or FieldInterface.Image or FieldInterface.Files;
}

/// <summary>A file field a body sends, with its locale when it sits in <c>translations</c>.</summary>
internal readonly record struct SentFile(FieldMetadata Field, string? Locale, JsonElement Value);
