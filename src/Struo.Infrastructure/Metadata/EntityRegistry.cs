using Struo.Application.Metadata;

namespace Struo.Infrastructure.Metadata;

public sealed class EntityRegistry : IEntityRegistry
{
    private readonly IReadOnlyDictionary<string, EntityDescriptor> _byCollection;

    public EntityRegistry(IReadOnlyDictionary<string, EntityDescriptor> descriptors) =>
        _byCollection = descriptors;

    public EntityDescriptor? Get(string collection) =>
        _byCollection.TryGetValue(collection, out var d) ? d : null;
}
