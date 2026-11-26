using Struo.Domain.Metadata.Enums;
using Struo.Domain.Metadata.Models;

namespace Struo.Application.Query;

/// <summary>The fields of a collection that link the <c>file</c> collection, as the relation write checks pick them.</summary>
internal static class FileFields
{
    /// <summary>True for a <c>File</c>, <c>Image</c> or <c>Files</c> field.</summary>
    internal static bool IsFileField(FieldMetadata field) =>
        field.Interface is FieldInterface.File or FieldInterface.Image or FieldInterface.Files;

    /// <summary>The file fields a body can bind on the item itself: not translatable, not server-managed.</summary>
    internal static IEnumerable<FieldMetadata> Root(CollectionMetadata meta) =>
        meta.Fields.Where(f => IsFileField(f) && !f.Translatable && !f.IsSystem && !f.ReadOnly);

    /// <summary>The translatable file fields, which live in the translation rows.</summary>
    internal static IEnumerable<FieldMetadata> Translated(CollectionMetadata meta) =>
        meta.Fields.Where(f => IsFileField(f) && f.Translatable);
}
