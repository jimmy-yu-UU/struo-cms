using Struo.Domain.Metadata.Models;

namespace Struo.Application.Metadata;

public interface IMetadataProvider
{
    IReadOnlyList<CollectionMetadata> GetCollections();
    CollectionMetadata? GetCollection(string name);
}
