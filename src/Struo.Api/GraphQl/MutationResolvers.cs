// src/Struo.Api/GraphQl/MutationResolvers.cs
using HotChocolate.Resolvers;
using HotChocolate.Types.Descriptors;
using HotChocolate.Types.Descriptors.Configurations;

namespace Struo.Api.GraphQl;

/// <summary>
/// Root mutation field configs + resolvers, one create/update/delete per collection. Mirrors
/// <see cref="CollectionResolvers"/> (queries). Resolvers convert the typed input to a JsonElement
/// and delegate to <see cref="IGraphQlDataSource"/> (which wraps ItemService); create/update re-read
/// the row so the returned node matches a query result.
/// </summary>
internal static class MutationResolvers
{
    internal static ObjectFieldConfiguration DeleteField(string collection)
    {
        var config = new ObjectFieldConfiguration(
            SchemaTypeMapper.DeleteFieldName(collection), null,
            TypeReference.Parse("Boolean"),
            resolver: ctx => ResolveDelete(ctx, collection));
        config.Arguments.Add(new ArgumentConfiguration("id", null, TypeReference.Parse("ID!")));
        return config;
    }

    private static async ValueTask<object?> ResolveDelete(IResolverContext ctx, string collection)
    {
        var id = ctx.ArgumentValue<string>("id");
        return await ctx.Service<IGraphQlDataSource>().DeleteAsync(collection, id, ctx.RequestAborted);
    }
}
