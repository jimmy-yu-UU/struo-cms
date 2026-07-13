// src/Struo.Api/GraphQl/CollectionResolvers.cs
using HotChocolate.Resolvers;
using HotChocolate.Types;
using HotChocolate.Types.Descriptors;
using HotChocolate.Types.Descriptors.Configurations;
using Struo.Application.Metadata;
using Struo.Domain.Metadata.Enums;
using Struo.Domain.Query;

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
        var metadata = ctx.Service<IMetadataProvider>();
        var deep = SelectionDeepSpec(ctx, collection, metadata, elementIsDirect: true);
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
        var deep = SelectionDeepSpec(ctx, collection, metadata, elementIsDirect: false);
        var query = GraphQlQueryBuilder.BuildQuery(
            filter, sort, limit, offset, search, deep,
            collection, RelationTargets(metadata));
        var page = await ctx.Service<IGraphQlDataSource>().QueryAsync(collection, query, locale, ctx.RequestAborted);
        return new PagedResultView(page.Data.Cast<object>().ToList(), page.Total);
    }

    /// <summary>
    /// Delegate for FilterInputTranslator: returns the target collection name when <paramref name="key"/>
    /// is a filterable relation of <paramref name="coll"/> (so a nested filter descends into a dotted
    /// path), else null. M2O participates only with a foreign key (its engine hop dereferences it);
    /// O2M and M2M participate unconditionally (they resolve via the reverse FK / junction) — to-many
    /// paths carry ANY/EXISTS semantics.
    /// </summary>
    private static Func<string, string, string?> RelationTargets(IMetadataProvider metadata) =>
        (coll, key) => metadata.GetCollection(coll)?.Relations
            .FirstOrDefault(r => string.Equals(r.Name, key, StringComparison.OrdinalIgnoreCase)
                && ((r.Kind == RelationKind.ManyToOne && r.ForeignKey is not null)
                    || r.Kind is RelationKind.OneToMany or RelationKind.ManyToMany))
            ?.TargetCollection;

    /// <summary>
    /// Builds a nested <see cref="DeepSpec"/> from the client's selection tree: each selected field
    /// that is a relation of the collection contributes a <see cref="DeepRelationSpec"/> whose nested
    /// <c>Deep</c> is built recursively from that relation's own sub-selection. Depth is bounded by
    /// ItemService's MaxRelationDepth validation and HotChocolate's max-execution-depth.
    /// </summary>
    internal static DeepSpec? SelectionDeepSpec(
        IResolverContext ctx, string collection, IMetadataProvider metadata, bool elementIsDirect)
    {
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
            if (itemsSel is null) return null;
            elementType = (ObjectType)itemsSel.Field.Type.NamedType();                  // X
            childSelections = ctx.GetSelections(elementType, itemsSel);
        }
        return BuildDeep(ctx, collection, childSelections, metadata);
    }

    private static DeepSpec? BuildDeep(
        IResolverContext ctx, string collection, SelectionEnumerator childSelections, IMetadataProvider metadata)
    {
        var relByName = metadata.GetCollection(collection)?.Relations
            .ToDictionary(r => r.Name, r => r, StringComparer.OrdinalIgnoreCase);
        if (relByName is null || relByName.Count == 0) return null;

        var map = new Dictionary<string, DeepRelationSpec>(StringComparer.OrdinalIgnoreCase);
        foreach (var sel in childSelections)
        {
            if (!relByName.TryGetValue(sel.Field.Name, out var rel)) continue;
            if (map.ContainsKey(rel.Name)) continue; // aliased-duplicate selections: first wins per level
            var targetType = (ObjectType)sel.Field.Type.NamedType();
            var nested = BuildDeep(ctx, rel.TargetCollection, ctx.GetSelections(targetType, sel), metadata);
            map[rel.Name] = new DeepRelationSpec(null, null, nested);
        }
        return map.Count == 0 ? null : new DeepSpec(map);
    }
}
