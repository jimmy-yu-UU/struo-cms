// tests/Struo.Tests/GraphQl/CoreGraphQlSchema.cs
using HotChocolate.Execution;
using HotChocolate.Serialization;
using Microsoft.Extensions.DependencyInjection;
using Struo.Api.GraphQl;
using Struo.Application.Metadata;
using Struo.Infrastructure.Metadata;

namespace Struo.Tests.GraphQl;

/// <summary>
/// The GraphQL schema the host serves when only the framework's own collections exist, built through
/// the same <c>AddStruoSchemaTypes</c> registration the host uses, from scanner-produced metadata and
/// without a database.
/// </summary>
internal static class CoreGraphQlSchema
{
    public static async Task<string> BuildSdlAsync()
    {
        var types = FrameworkEntityTypes.All;
        var collections = MetadataScanner.ScanTypes(types);

        var executor = await new ServiceCollection()
            .AddSingleton<IMetadataProvider>(new CachedMetadataProvider(collections))
            .AddSingleton<IEntityRegistry>(new EntityRegistry(MetadataScanner.ScanDescriptors(types)))
            .AddSingleton<IM2MDescriptorSource>(
                new RelationshipGraph(collections, MetadataScanner.CollectionTypeMap(types)))
            .AddScoped<IGraphQlDataSource, FakeGraphQlDataSource>()
            .AddSingleton<StruoTypeModule>()
            .AddGraphQLServer()
            .AddStruoSchemaTypes()
            .BuildRequestExecutorAsync();

        return SchemaFormatter.FormatAsString(executor.Schema);
    }
}
