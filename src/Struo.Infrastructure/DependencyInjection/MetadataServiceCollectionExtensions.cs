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
        // SafeGetTypes tolerates an assembly with an unresolvable type (audit A4).
        var allTypes = allAssemblies.SelectMany(MetadataScanner.SafeGetTypes).ToList();

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
    /// <remarks>
    /// Reads <paramref name="config"/> synchronously at registration time — before the host's
    /// <c>WebApplicationBuilder.Build()</c> runs — so only configuration sources already installed on
    /// the passed <see cref="IConfiguration"/> are visible to this scan. Sources spliced in later, such
    /// as <c>WebApplicationFactory.ConfigureAppConfiguration</c> or <c>IWebHostBuilder.UseSetting</c>,
    /// are NOT visible here. Integration tests that need to select a content assembly must supply it
    /// through an environment variable instead (e.g. <c>Struo__ContentAssemblies__0</c>), set before the
    /// host process starts.
    /// </remarks>
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
