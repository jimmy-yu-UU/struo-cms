// src/Struo.Api/GraphQl/MutationResolvers.cs
using HotChocolate.Language;
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
        var body = MutationInputMapper.ToJsonElement(SentFieldsOnly(ctx, "input", input));

        var source = ctx.Service<IGraphQlDataSource>();
        var created = await source.CreateAsync(collection, body, ctx.RequestAborted);

        // Re-read so the returned node matches a query result (relations/translations resolvable).
        var id = created.GetValueOrDefault("id")?.ToString();
        if (id is null) return created;
        var relations = CollectionResolvers.SelectionRelations(ctx, collection, elementIsDirect: true);
        var deep = GraphQlQueryBuilder.BuildQuery(null, null, null, null, null, relations).Deep;
        return await source.GetAsync(collection, id, deep, locale, ctx.RequestAborted);
    }

    internal static ObjectFieldConfiguration UpdateField(string collection, IMetadataProvider metadata)
    {
        var config = new ObjectFieldConfiguration(
            SchemaTypeMapper.UpdateFieldName(collection), null,
            TypeReference.Parse(SchemaTypeMapper.TypeName(collection)),
            resolver: ctx => ResolveUpdate(ctx, collection));
        config.Arguments.Add(new ArgumentConfiguration("id", null, TypeReference.Parse("ID!")));
        config.Arguments.Add(new ArgumentConfiguration(
            "input", null, TypeReference.Parse(SchemaTypeMapper.UpdateInputName(collection) + "!")));
        config.Arguments.Add(new ArgumentConfiguration("locale", null, TypeReference.Parse("String")));
        return config;
    }

    private static async ValueTask<object?> ResolveUpdate(IResolverContext ctx, string collection)
    {
        var id = ctx.ArgumentValue<string>("id");
        var input = ctx.ArgumentValue<IReadOnlyDictionary<string, object?>?>("input");
        var locale = ctx.ArgumentValue<string?>("locale");
        var body = MutationInputMapper.ToJsonElement(SentFieldsOnly(ctx, "input", input));

        var source = ctx.Service<IGraphQlDataSource>();
        var updated = await source.UpdateAsync(collection, id, body, ctx.RequestAborted);
        if (updated is null) return null; // unknown id -> null (REST 404 parity)

        var relations = CollectionResolvers.SelectionRelations(ctx, collection, elementIsDirect: true);
        var deep = GraphQlQueryBuilder.BuildQuery(null, null, null, null, null, relations).Deep;
        return await source.GetAsync(collection, id, deep, locale, ctx.RequestAborted);
    }

    // HotChocolate's coerced argument dictionary (ctx.ArgumentValue<IReadOnlyDictionary<string,object?>?>)
    // always contains EVERY declared input field for a Dictionary-runtime-type InputObjectType — an
    // optional field the client never sent is backfilled with null rather than omitted. That breaks
    // update's partial merge (ItemService.UpdateAsync treats "key present" as "client sent it" — see
    // bodyKeys in ItemService.UpdateAsync — so a backfilled null would silently overwrite a field the
    // client never touched) AND create's defaults (ItemService.CreateAsync deserializes the body onto
    // a fresh entity, so a backfilled null overwrites a field's CLR default, e.g. Article.Status, and
    // can 500 on a non-nullable value-type scalar the client omitted). Both resolvers route the
    // coerced dict through this helper. The request's argument LITERAL (post-variable-substitution)
    // still reflects only the client-supplied keys, so intersect the coerced dict's values against the
    // literal's field names to recover the true sent-fields set without needing to re-derive values
    // from the literal ourselves.
    private static IReadOnlyDictionary<string, object?>? SentFieldsOnly(
        IResolverContext ctx, string argumentName, IReadOnlyDictionary<string, object?>? coerced)
    {
        if (coerced is null) return null;
        if (ctx.ArgumentLiteral<IValueNode>(argumentName) is not ObjectValueNode literal) return coerced;
        var sentKeys = literal.Fields.Select(f => f.Name.Value).ToHashSet(StringComparer.Ordinal);
        return coerced.Where(kv => sentKeys.Contains(kv.Key)).ToDictionary(kv => kv.Key, kv => kv.Value);
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
