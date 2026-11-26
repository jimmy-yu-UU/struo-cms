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
/// included), a many-to-one foreign key (null included) and a <c>File</c>/<c>Image</c>/<c>Files</c> field,
/// which links the <c>file</c> collection. A key the body omits needs nothing. The check runs before any id
/// is parsed or looked up, so the answer is the same for an existing, a missing and a malformed id.
/// </summary>
public sealed class RelationTargetReadGuard(IM2MDescriptorSource m2mSource, IPermissionService permissions)
{
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
        foreach (var desc in m2mSource.M2MDescriptors(meta.Name))
        {
            if (JsonBodyUtil.TryGetRelationArray(body, desc.RelationName, out _)) yield return desc.TargetCollection;
        }

        var sent = ItemDeserializer.PresentNames(body);
        foreach (var rel in meta.Relations)
        {
            if (rel is { Kind: RelationKind.ManyToOne, ForeignKey: { } key } && sent.Contains(key))
                yield return rel.TargetCollection;
        }

        if (meta.Fields.Any(field => IsSentFileField(field, sent))) yield return FileCollection.Name;
    }

    private static bool IsSentFileField(FieldMetadata field, HashSet<string> sent) =>
        field is { Interface: FieldInterface.File or FieldInterface.Image or FieldInterface.Files }
        && !field.Translatable && !field.IsSystem && !field.ReadOnly
        && sent.Contains(field.Name);
}
