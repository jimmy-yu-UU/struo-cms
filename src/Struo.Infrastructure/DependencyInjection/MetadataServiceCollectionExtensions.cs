using System.Reflection;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Struo.Application.Metadata;
using Struo.Domain.Metadata;
using Struo.Infrastructure.Metadata;

namespace Struo.Infrastructure.DependencyInjection;

public static class MetadataServiceCollectionExtensions
{
    public static IServiceCollection AddStruoMetadata(
        this IServiceCollection services, params Assembly[] assemblies)
    {
        // Include the framework's own assembly (Language and other built-ins) alongside caller assemblies.
        var allAssemblies = assemblies
            .Append(typeof(MetadataServiceCollectionExtensions).Assembly)
            .Distinct().ToArray();

        // Eager scan at registration -> immutable singleton. No per-request reflection.
        var allTypes = allAssemblies.SelectMany(a => a.GetTypes()).ToList();

        var collections = MetadataScanner.Scan(allAssemblies);
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

        // Register graph as IRelationshipGraph, IM2MDescriptorSource, and the concrete RelationshipGraph
        var graph = new RelationshipGraph(collections, collectionTypes);
        services.AddSingleton<IRelationshipGraph>(graph);
        services.AddSingleton<IM2MDescriptorSource>(graph);
        services.AddSingleton(graph);

        services.AddSingleton<IEntityTypeCollector, EntityTypeCollector>();

        return services;
    }

    /// <summary>
    /// Convention-based registration used by the host. Scans: the framework assembly (always,
    /// appended by the params overload), the <paramref name="hostAssemblies"/> passed explicitly by
    /// the host (e.g. <c>typeof(Program).Assembly</c>), and every assembly named under
    /// <c>Struo:ContentAssemblies</c> in configuration. A named assembly that cannot be loaded is a
    /// fail-fast <see cref="MetadataException"/> — never silently skipped.
    /// </summary>
    public static IServiceCollection AddStruoMetadata(
        this IServiceCollection services, IConfiguration config, params Assembly[] hostAssemblies)
    {
        var names = config.GetSection("Struo:ContentAssemblies").Get<string[]>() ?? [];
        var assemblies = new List<Assembly>(hostAssemblies);

        foreach (var name in names)
        {
            try
            {
                assemblies.Add(Assembly.Load(new AssemblyName(name)));
            }
            catch (Exception ex)
            {
                throw new MetadataException(
                    $"Struo:ContentAssemblies entry '{name}' could not be loaded: {ex.Message}");
            }
        }

        return services.AddStruoMetadata(assemblies.ToArray());
    }
}
