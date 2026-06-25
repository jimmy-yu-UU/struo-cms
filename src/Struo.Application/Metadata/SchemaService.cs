// src/Struo.Application/Metadata/SchemaService.cs
using Struo.Domain.Metadata.Models;

namespace Struo.Application.Metadata;

// Thin use-case seam; Phase 6 adds role-based field filtering here.
public sealed class SchemaService(IMetadataProvider provider)
{
    public IReadOnlyList<CollectionMetadata> GetAll() => provider.GetCollections();
    public CollectionMetadata? Get(string collection) => provider.GetCollection(collection);
}
