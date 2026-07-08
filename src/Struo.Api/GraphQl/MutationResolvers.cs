// src/Struo.Api/GraphQl/MutationResolvers.cs
using HotChocolate.Resolvers;
using HotChocolate.Types.Descriptors;
using HotChocolate.Types.Descriptors.Configurations;
using Struo.Application.Metadata;

namespace Struo.Api.GraphQl;

/// <summary>
/// Root mutation field configs + resolvers, one create/update/delete per collection. Mirrors
/// <see cref="CollectionResolvers"/> (queries). Resolvers convert the typed input to a JsonElement
/// and delegate to <see cref="IGraphQlDataSource"/> (which wraps ItemService); create/update re-read
/// the row so the returned node matches a query result.
/// </summary>
internal static class MutationResolvers
{
    internal static ObjectFieldConfiguration CreateField(string collection, IMetadataProvider metadata)
    {
        var config = new ObjectFieldConfiguration(
            SchemaTypeMapper.CreateFieldName(collection), null,
            TypeReference.Parse(SchemaTypeMapper.TypeName(collection)),
            resolver: ctx => ResolveCreate(ctx, collection));
        config.Arguments.Add(new ArgumentConfiguration(
            "input", null, TypeReference.Parse(SchemaTypeMapper.CreateInputName(collection) + "!")));
        config.Arguments.Add(new ArgumentConfiguration("locale", null, TypeReference.Parse("String")));
        return config;
    }

    private static async ValueTask<object?> ResolveCreate(IResolverContext ctx, string collection)
    {
        var input = ctx.ArgumentValue<IReadOnlyDictionary<string, object?>?>("input");
        var locale = ctx.ArgumentValue<string?>("locale");
        var body = MutationInputMapper.ToJsonElement(input);

        var source = ctx.Service<IGraphQlDataSource>();
        var created = await source.CreateAsync(collection, body, ctx.RequestAborted);

        // Re-read so the returned node matches a query result (relations/translations resolvable).
        var id = created.GetValueOrDefault("id")?.ToString();
        if (id is null) return created;
        var relations = CollectionResolvers.SelectionRelations(ctx, collection, elementIsDirect: true);
        var deep = GraphQlQueryBuilder.BuildQuery(null, null, null, null, null, relations).Deep;
        return await source.GetAsync(collection, id, deep, locale, ctx.RequestAborted);
    }

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
