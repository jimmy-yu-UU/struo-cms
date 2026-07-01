using Struo.Application.Metadata;

namespace Struo.Infrastructure.Metadata;

public sealed class EntityTypeCollector(
    IMetadataProvider metadata,
    IEntityRegistry registry,
    IM2MDescriptorSource m2mDescriptors) : IEntityTypeCollector
{
    public IReadOnlyList<Type> CollectForInitTables()
    {
        var types = new HashSet<Type>();

        foreach (var collection in metadata.GetCollections())
        {
            var descriptor = registry.Get(collection.Name);
            if (descriptor is not null)
                types.Add(descriptor.EntityType);

            if (collection.Translation is not null)
                types.Add(collection.Translation.TranslationEntityType);

            foreach (var junction in m2mDescriptors.M2MDescriptors(collection.Name))
                types.Add(junction.JunctionType);
        }

        foreach (var frameworkType in FrameworkEntityTypes.All)
            types.Add(frameworkType);

        return types.ToArray();
    }
}
