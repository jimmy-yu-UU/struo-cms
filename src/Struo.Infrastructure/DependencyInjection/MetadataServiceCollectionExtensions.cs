using System.Reflection;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Struo.Application.Metadata;
using Struo.Infrastructure.Metadata;

namespace Struo.Infrastructure.DependencyInjection;

public static class MetadataServiceCollectionExtensions
{
    public static IServiceCollection AddStruoMetadata(
        this IServiceCollection services, params Assembly[] assemblies)
    {
        // Eager scan at registration -> immutable singleton. No per-request reflection.
        var allTypes = assemblies.SelectMany(a => a.GetTypes()).ToList();

        var collections = MetadataScanner.Scan(assemblies);
        services.AddSingleton<IMetadataProvider>(new CachedMetadataProvider(collections));

        var descriptors = MetadataScanner.ScanDescriptors(allTypes);
        services.AddSingleton<IEntityRegistry>(new EntityRegistry(descriptors));

        // Build camelName → Type map for the relationship graph constructor
        var collectionTypes = allTypes
            .Where(t => t.GetCustomAttribute<Struo.Domain.Metadata.Attributes.CmsCollectionAttribute>() is not null)
            .ToDictionary(
                t => JsonNamingPolicy.CamelCase.ConvertName(t.Name),
                t => t,
                StringComparer.OrdinalIgnoreCase);

        // Register graph as both IRelationshipGraph and the concrete RelationshipGraph
        var graph = new RelationshipGraph(collections, collectionTypes);
        services.AddSingleton<IRelationshipGraph>(graph);
        services.AddSingleton(graph);

        return services;
    }
}
