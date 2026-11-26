// src/Struo.Application/Query/Write/RelationTargetReadGuard.cs
using System.Text.Json;
using Struo.Application.Files;
using Struo.Application.Metadata;
using Struo.Application.Security;
using Struo.Domain.Metadata.Enums;
using Struo.Domain.Metadata.Models;
using Struo.Domain.Query;

namespace Struo.Application.Query;

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

    /// <summary>Throws <see cref="PermissionDeniedException"/> for the first sent link whose target the caller cannot read.</summary>
    public void Require(CollectionMetadata meta, JsonElement body)
    {
        if (body.ValueKind != JsonValueKind.Object) return;
        foreach (var target in SentTargets(meta, body))
        {
            if (!permissions.CanRead(target)) throw ReadDenied(target);
        }
    }

    private IEnumerable<string> SentTargets(CollectionMetadata meta, JsonElement body)
    {
        var sent = ItemDeserializer.PresentNames(body);
        return ManyToManyTargets(meta, body)
            .Concat(ManyToOneTargets(meta, sent))
            .Concat(JunctionTargets(meta, sent))
            .Concat(FileTargets(meta, body, sent));
    }

    // A many-to-many key is matched by its exact name and a foreign key ignoring case: each mirrors how its
    // consumer reads it (the relation sync looks the array up by name, the deserializer binds keys ignoring case).
    private IEnumerable<string> ManyToManyTargets(CollectionMetadata meta, JsonElement body) =>
        m2mSource.M2MDescriptors(meta.Name)
            .Where(desc => JsonBodyUtil.TryGetRelationArray(body, desc.RelationName, out _))
            .Select(desc => desc.TargetCollection);

    private static IEnumerable<string> ManyToOneTargets(CollectionMetadata meta, HashSet<string> sent) =>
        meta.Relations
            .Where(rel => rel is { Kind: RelationKind.ManyToOne, ForeignKey: { } key } && sent.Contains(key))
            .Select(rel => rel.TargetCollection);

    // The keys of a junction collection are plain fields, not relations; each links the collection it points at.
    private IEnumerable<string> JunctionTargets(CollectionMetadata meta, HashSet<string> sent)
    {
        foreach (var link in m2mSource.JunctionKeyLinks(meta.Name))
        {
            if (sent.Contains(link.ParentKeyProperty)) yield return link.ParentCollection;
            if (sent.Contains(link.TargetKeyProperty)) yield return link.TargetCollection;
        }
    }

    private static IEnumerable<string> FileTargets(CollectionMetadata meta, JsonElement body, HashSet<string> sent)
    {
        if (SendsRootFile(meta, sent) || SendsTranslatedFile(meta, body)) yield return FileCollection.Name;
    }

    private static bool SendsRootFile(CollectionMetadata meta, HashSet<string> sent) =>
        meta.Fields.Any(field =>
            IsFileField(field) && !field.Translatable && !field.IsSystem && !field.ReadOnly && sent.Contains(field.Name));

    // The sidecar sync addresses a translated field by name ignoring case, under the exact key "translations".
    private static bool SendsTranslatedFile(CollectionMetadata meta, JsonElement body)
    {
        if (!body.TryGetProperty(TranslationsKey, out var translations) || translations.ValueKind != JsonValueKind.Object)
            return false;
        var names = meta.Fields
            .Where(field => IsFileField(field) && field.Translatable)
            .Select(field => field.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        return names.Count > 0 && translations.EnumerateObject()
            .Where(locale => locale.Value.ValueKind == JsonValueKind.Object)
            .SelectMany(locale => locale.Value.EnumerateObject())
            .Any(field => names.Contains(field.Name));
    }

    private static bool IsFileField(FieldMetadata field) =>
        field.Interface is FieldInterface.File or FieldInterface.Image or FieldInterface.Files;
}
