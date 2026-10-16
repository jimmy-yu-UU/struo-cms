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
    private const string RevisionNumberKey = "revisionNumber";
    private const string OperationKey = "operation";
    private const string CreatedAtKey = "createdAt";
    private const string CreatedByKey = "createdBy";
    private const string SourceRevisionNumberKey = "sourceRevisionNumber";
    private const string SnapshotKey = "snapshot";

    internal static ObjectType RevisionType()
    {
        var config = new ObjectTypeConfiguration("Revision", null, typeof(IReadOnlyDictionary<string, object?>));
        config.Fields.Add(CollectionSchemaBuilder.Field(RevisionNumberKey, "Int!",
            ctx => ctx.Parent<IReadOnlyDictionary<string, object?>>().GetValueOrDefault(RevisionNumberKey)));
        config.Fields.Add(CollectionSchemaBuilder.Field(OperationKey, "String!",
            ctx => ctx.Parent<IReadOnlyDictionary<string, object?>>().GetValueOrDefault(OperationKey)));
        config.Fields.Add(CollectionSchemaBuilder.Field(CreatedAtKey, "DateTime!",
            ctx => ctx.Parent<IReadOnlyDictionary<string, object?>>().GetValueOrDefault(CreatedAtKey)));
        config.Fields.Add(CollectionSchemaBuilder.Field(CreatedByKey, "ID",
            ctx => ctx.Parent<IReadOnlyDictionary<string, object?>>().GetValueOrDefault(CreatedByKey)));
        // Nullable: only reverts populate this (the revision number they restored); every other
        // operation stores NULL. Int (not Long) to match the sibling `revisionNumber` field above
        // and the `revisionNumber` argument on RevisionField, since this carries the same domain
        // value — a revision number a caller feeds straight back into articleRevision(revisionNumber:).
        config.Fields.Add(CollectionSchemaBuilder.Field(SourceRevisionNumberKey, "Int",
            ctx => ctx.Parent<IReadOnlyDictionary<string, object?>>().GetValueOrDefault(SourceRevisionNumberKey)));
        config.Fields.Add(CollectionSchemaBuilder.Field(SnapshotKey, "Any",
            ctx => ctx.Parent<IReadOnlyDictionary<string, object?>>().GetValueOrDefault(SnapshotKey)));
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
        config.Arguments.Add(new ArgumentConfiguration(RevisionNumberKey, null, TypeReference.Parse("Int!")));
        return config;
    }

    private static async ValueTask<object?> ResolveList(IResolverContext ctx, string collection)
    {
        var id = ctx.ArgumentValue<string>("id");
        var list = await ctx.Service<IGraphQlDataSource>().ListRevisionsAsync(collection, id, ctx.RequestAborted);
        if (list is null) return new List<IReadOnlyDictionary<string, object?>>();   // hidden/unknown item: empty, like a non-revisioned collection
        return list.Select(r => (IReadOnlyDictionary<string, object?>)new Dictionary<string, object?>
        {
            [RevisionNumberKey] = r.RevisionNumber,
            [OperationKey] = r.Operation,
            [CreatedAtKey] = r.CreatedAt,
            [CreatedByKey] = r.CreatedBy,
            [SourceRevisionNumberKey] = r.SourceRevisionNumber,
            [SnapshotKey] = null,                 // list = metadata only
        }).ToList();
    }

    private static async ValueTask<object?> ResolveSingle(IResolverContext ctx, string collection)
    {
        var id = ctx.ArgumentValue<string>("id");
        var n = (long)ctx.ArgumentValue<int>(RevisionNumberKey);
        var rec = await ctx.Service<IGraphQlDataSource>().GetRevisionAsync(collection, id, n, ctx.RequestAborted);
        if (rec is null) return null;
        object? snapshot;
        try { snapshot = JsonSerializer.Deserialize<JsonElement>(rec.Snapshot); }
        catch (JsonException) { snapshot = rec.Snapshot; }
        return new Dictionary<string, object?>
        {
            [RevisionNumberKey] = rec.RevisionNumber,
            [OperationKey] = rec.Operation,
            [CreatedAtKey] = rec.CreatedAt,
            [CreatedByKey] = rec.CreatedBy,
            [SourceRevisionNumberKey] = rec.SourceRevisionNumber,
            [SnapshotKey] = snapshot,
        };
    }
}
