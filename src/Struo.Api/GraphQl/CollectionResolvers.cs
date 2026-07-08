// src/Struo.Api/GraphQl/CollectionResolvers.cs
using HotChocolate.Resolvers;
using HotChocolate.Types;
using HotChocolate.Types.Descriptors;
using HotChocolate.Types.Descriptors.Configurations;
using Struo.Application.Metadata;

namespace Struo.Api.GraphQl;

internal static class CollectionResolvers
{
    internal static ObjectFieldConfiguration SingleField(string collection, IMetadataProvider metadata)
    {
        var config = new ObjectFieldConfiguration(
            SchemaTypeMapper.SingleFieldName(collection), null,
            TypeReference.Parse(SchemaTypeMapper.TypeName(collection)),
            resolver: ctx => ResolveSingle(ctx, collection));
        config.Arguments.Add(new ArgumentConfiguration("id", null, TypeReference.Parse("ID!")));
        config.Arguments.Add(new ArgumentConfiguration("locale", null, TypeReference.Parse("String")));
        return config;
    }

    internal static ObjectFieldConfiguration ListField(string collection, IMetadataProvider metadata)
    {
        var config = new ObjectFieldConfiguration(
            SchemaTypeMapper.ListFieldName(collection), null,
            TypeReference.Parse(SchemaTypeMapper.TypeName(collection) + "List!"),
            resolver: ctx => ResolveList(ctx, collection, metadata));
        config.Arguments.Add(new ArgumentConfiguration("filter", null, TypeReference.Parse(SchemaTypeMapper.TypeName(collection) + "FilterInput")));
        config.Arguments.Add(new ArgumentConfiguration("sort", null, TypeReference.Parse("[String!]")));
        config.Arguments.Add(new ArgumentConfiguration("limit", null, TypeReference.Parse("Int")));
        config.Arguments.Add(new ArgumentConfiguration("offset", null, TypeReference.Parse("Int")));
        config.Arguments.Add(new ArgumentConfiguration("search", null, TypeReference.Parse("String")));
        config.Arguments.Add(new ArgumentConfiguration("locale", null, TypeReference.Parse("String")));
        return config;
    }

    private static async ValueTask<object?> ResolveSingle(IResolverContext ctx, string collection)
    {
        var id = ctx.ArgumentValue<string>("id");
        var locale = ctx.ArgumentValue<string?>("locale");
        var relations = SelectionRelations(ctx, collection, elementIsDirect: true);
        var deep = GraphQlQueryBuilder.BuildQuery(null, null, null, null, null, relations).Deep;
        var data = await ctx.Service<IGraphQlDataSource>().GetAsync(collection, id, deep, locale, ctx.RequestAborted);
        return data;
    }

    private static async ValueTask<object?> ResolveList(IResolverContext ctx, string collection, IMetadataProvider metadata)
    {
        var filter = ctx.ArgumentValue<IReadOnlyDictionary<string, object?>?>("filter");
        var sort = ctx.ArgumentValue<IReadOnlyList<string>?>("sort");
        var limit = ctx.ArgumentValue<int?>("limit");
        var offset = ctx.ArgumentValue<int?>("offset");
        var search = ctx.ArgumentValue<string?>("search");
        var locale = ctx.ArgumentValue<string?>("locale");
        var relations = SelectionRelations(ctx, collection, elementIsDirect: false);
        var query = GraphQlQueryBuilder.BuildQuery(filter, sort, limit, offset, search, relations);
        var page = await ctx.Service<IGraphQlDataSource>().QueryAsync(collection, query, locale, ctx.RequestAborted);
        return new PagedResultView(page.Data.Cast<object>().ToList(), page.Total);
    }

    /// <summary>Which of the collection's relations the client selected on the element type.</summary>
    internal static IReadOnlyList<string> SelectionRelations(IResolverContext ctx, string collection, bool elementIsDirect)
    {
        var relNames = ctx.Service<IMetadataProvider>().GetCollection(collection)?.Relations
            .Select(r => r.Name).ToHashSet(StringComparer.OrdinalIgnoreCase) ?? [];
        if (relNames.Count == 0) return [];

        ObjectType elementType;
        SelectionEnumerator childSelections;
        if (elementIsDirect)
        {
            elementType = (ObjectType)ctx.Selection.Field.Type.NamedType();
            childSelections = ctx.GetSelections(elementType);
        }
        else
        {
            var listType = (ObjectType)ctx.Selection.Field.Type.NamedType();          // XList
            var itemsSel = ctx.GetSelections(listType).FirstOrDefault(s => s.Field.Name == "items");
            if (itemsSel is null) return [];
            elementType = (ObjectType)itemsSel.Field.Type.NamedType();                  // X
            childSelections = ctx.GetSelections(elementType, itemsSel);
        }
        // Aliased duplicate selections of the same relation (e.g. `a: category { ... } b: category
        // { ... }`) stay distinct child selections with the SAME Field.Name — HotChocolate only
        // merges non-aliased duplicates. Without deduping, BuildQuery's requestedRelations.ToDictionary
        // throws ArgumentException on the duplicate key. Distinct with OrdinalIgnoreCase matches the
        // relNames HashSet's comparer and BuildQuery's DeepSpec dictionary comparer.
        return childSelections.Select(s => s.Field.Name).Where(relNames.Contains)
            .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }
}
