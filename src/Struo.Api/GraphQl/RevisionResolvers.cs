// src/Struo.Api/GraphQl/RevisionResolvers.cs
using System.Text.Json;
using HotChocolate.Resolvers;
using HotChocolate.Types;
using HotChocolate.Types.Descriptors;
using HotChocolate.Types.Descriptors.Configurations;
using Struo.Application.Revisions;

namespace Struo.Api.GraphQl;

/// <summary>
/// The shared `Revision` object type + per-collection `xRevisions`/`xRevision` query field configs.
/// A Revision node is represented as an IReadOnlyDictionary (like Translation) so the fields resolve
/// uniformly. `snapshot` is null on the list (metadata only) and populated by the single query —
/// mirroring REST (list = metadata, get = snapshot).
/// </summary>
internal static class RevisionResolvers
{
    internal static ObjectType RevisionType()
    {
        var config = new ObjectTypeConfiguration("Revision", null, typeof(IReadOnlyDictionary<string, object?>));
        config.Fields.Add(CollectionSchemaBuilder.Field("revisionNumber", "Int!",
            ctx => ctx.Parent<IReadOnlyDictionary<string, object?>>().GetValueOrDefault("revisionNumber")));
        config.Fields.Add(CollectionSchemaBuilder.Field("operation", "String!",
            ctx => ctx.Parent<IReadOnlyDictionary<string, object?>>().GetValueOrDefault("operation")));
        config.Fields.Add(CollectionSchemaBuilder.Field("createdAt", "DateTime!",
            ctx => ctx.Parent<IReadOnlyDictionary<string, object?>>().GetValueOrDefault("createdAt")));
        config.Fields.Add(CollectionSchemaBuilder.Field("createdBy", "ID",
            ctx => ctx.Parent<IReadOnlyDictionary<string, object?>>().GetValueOrDefault("createdBy")));
        // Nullable: only reverts populate this (the revision number they restored); every other
        // operation stores NULL.
        config.Fields.Add(CollectionSchemaBuilder.Field("sourceRevisionNumber", "Long",
            ctx => ctx.Parent<IReadOnlyDictionary<string, object?>>().GetValueOrDefault("sourceRevisionNumber")));
        config.Fields.Add(CollectionSchemaBuilder.Field("snapshot", "Any",
            ctx => ctx.Parent<IReadOnlyDictionary<string, object?>>().GetValueOrDefault("snapshot")));
        return ObjectType.CreateUnsafe(config);
    }

    internal static ObjectFieldConfiguration RevisionsField(string collection)
    {
        var config = new ObjectFieldConfiguration(
            SchemaTypeMapper.RevisionsFieldName(collection), null,
            TypeReference.Parse("[Revision!]!"),
            resolver: ctx => ResolveList(ctx, collection));
        config.Arguments.Add(new ArgumentConfiguration("id", null, TypeReference.Parse("ID!")));
        return config;
    }

    internal static ObjectFieldConfiguration RevisionField(string collection)
    {
        var config = new ObjectFieldConfiguration(
            SchemaTypeMapper.RevisionFieldName(collection), null,
            TypeReference.Parse("Revision"),
            resolver: ctx => ResolveSingle(ctx, collection));
        config.Arguments.Add(new ArgumentConfiguration("id", null, TypeReference.Parse("ID!")));
        config.Arguments.Add(new ArgumentConfiguration("revisionNumber", null, TypeReference.Parse("Int!")));
        return config;
    }

    private static async ValueTask<object?> ResolveList(IResolverContext ctx, string collection)
    {
        var id = ctx.ArgumentValue<string>("id");
        var list = await ctx.Service<IGraphQlDataSource>().ListRevisionsAsync(collection, id, ctx.RequestAborted);
        return list.Select(r => (IReadOnlyDictionary<string, object?>)new Dictionary<string, object?>
        {
            ["revisionNumber"] = r.RevisionNumber,
            ["operation"] = r.Operation,
            ["createdAt"] = r.CreatedAt,
            ["createdBy"] = r.CreatedBy,
            ["sourceRevisionNumber"] = r.SourceRevisionNumber,
            ["snapshot"] = null,                 // list = metadata only
        }).ToList();
    }

    private static async ValueTask<object?> ResolveSingle(IResolverContext ctx, string collection)
    {
        var id = ctx.ArgumentValue<string>("id");
        var n = (long)ctx.ArgumentValue<int>("revisionNumber");
        var rec = await ctx.Service<IGraphQlDataSource>().GetRevisionAsync(collection, id, n, ctx.RequestAborted);
        if (rec is null) return null;
        object? snapshot;
        try { snapshot = JsonSerializer.Deserialize<JsonElement>(rec.Snapshot); }
        catch (JsonException) { snapshot = rec.Snapshot; }
        return new Dictionary<string, object?>
        {
            ["revisionNumber"] = rec.RevisionNumber,
            ["operation"] = rec.Operation,
            ["createdAt"] = rec.CreatedAt,
            ["createdBy"] = rec.CreatedBy,
            ["sourceRevisionNumber"] = rec.SourceRevisionNumber,
            ["snapshot"] = snapshot,
        };
    }
}
