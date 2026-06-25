using System.Reflection;
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
        var collections = MetadataScanner.Scan(assemblies);
        services.AddSingleton<IMetadataProvider>(new CachedMetadataProvider(collections));

        var descriptors = MetadataScanner.ScanDescriptors(assemblies.SelectMany(a => a.GetTypes()));
        services.AddSingleton<IEntityRegistry>(new EntityRegistry(descriptors));
        return services;
    }
}
