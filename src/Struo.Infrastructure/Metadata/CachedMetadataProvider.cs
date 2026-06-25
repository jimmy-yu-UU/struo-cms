using Struo.Application.Metadata;
using Struo.Domain.Metadata.Models;

namespace Struo.Infrastructure.Metadata;

public sealed class CachedMetadataProvider : IMetadataProvider
{
    private readonly IReadOnlyList<CollectionMetadata> _all;
    private readonly IReadOnlyDictionary<string, CollectionMetadata> _byName;

    public CachedMetadataProvider(IReadOnlyList<CollectionMetadata> collections)
    {
        _all = collections;
        _byName = collections.ToDictionary(c => c.Name, StringComparer.OrdinalIgnoreCase);
    }

    public IReadOnlyList<CollectionMetadata> GetCollections() => _all;

    public CollectionMetadata? GetCollection(string name) =>
        _byName.TryGetValue(name, out var c) ? c : null;
}
