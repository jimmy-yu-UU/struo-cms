// src/Struo.Application/Metadata/SchemaService.cs
using Struo.Domain.Metadata.Models;

namespace Struo.Application.Metadata;

// Thin use-case seam; Phase 6 adds role-based field filtering here.
public sealed class SchemaService(IMetadataProvider provider)
{
    public IReadOnlyList<CollectionMetadata> GetAll() => provider.GetCollections()
        .Select(WithoutHiddenFields).ToList();

    public CollectionMetadata? Get(string collection)
    {
        var meta = provider.GetCollection(collection);
        return meta is null ? null : WithoutHiddenFields(meta);
    }

    /// <summary>
    /// Strips <see cref="FieldMetadata.Hidden"/> fields before exposing metadata to callers.
    /// Hidden fields (e.g. Password, AccessToken) must never be projected or accepted by the
    /// generic CRUD path; removing them from the schema prevents UI/clients from discovering them.
    /// </summary>
    private static CollectionMetadata WithoutHiddenFields(CollectionMetadata meta) =>
        meta with { Fields = meta.Fields.Where(f => !f.Hidden).ToList() };
}
