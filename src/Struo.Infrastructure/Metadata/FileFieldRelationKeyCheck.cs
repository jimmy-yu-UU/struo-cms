using System.Reflection;
using Struo.Domain.Metadata;
using Struo.Domain.Metadata.Attributes;
using Struo.Domain.Metadata.Enums;

namespace Struo.Infrastructure.Metadata;

/// <summary>
/// The scan-time check that the key of a many-to-one relation is not also a <c>File</c> or <c>Image</c> field.
/// Such a field already resolves to the file: REST returns the id, GraphQL the <c>File</c> object, and the
/// write checks of the file field apply. A relation on the same key is a second declaration of the same link,
/// whatever its name and target, and the GraphQL object type would carry two fields for it.
/// Why: docs/ai/decisions/relation-key-and-primary-key-declarations.md
/// </summary>
internal static class FileFieldRelationKeyCheck
{
    /// <summary>
    /// Refuses the relation <paramref name="relation"/> when its key <paramref name="key"/> carries
    /// <c>[CmsField]</c> with a <c>File</c> or <c>Image</c> interface.
    /// </summary>
    public static void Require(PropertyInfo relation, PropertyInfo key)
    {
        if (key.GetCustomAttribute<CmsFieldAttribute>() is not { Interface: FieldInterface.File or FieldInterface.Image } field)
            return;

        var holder = key.DeclaringType!.Name;
        throw new MetadataException(
            $"Relation '{RelationDeclarationCheck.RelationName(relation)}' uses '{holder}.{key.Name}' as its key, " +
            $"but '{key.Name}' is a [CmsField(Interface = {field.Interface})] property: a File or Image field already " +
            "resolves to the file (REST returns the id, GraphQL the File object, and the write checks of the file " +
            "field apply), so the relation is a second declaration of the same link. Remove [CmsRelation] and " +
            $"[Navigate] from '{relation.Name}', or give '{key.Name}' a non-file interface such as Uuid if the target " +
            "is not the file collection.");
    }
}
