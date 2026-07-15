// src/Struo.Api/GraphQl/StruoTypeModule.cs
using HotChocolate.Execution.Configuration;
using HotChocolate.Types;
using HotChocolate.Types.Descriptors;
using HotChocolate.Types.Descriptors.Configurations;
using Struo.Application.Metadata;
using Struo.Domain.Query;

namespace Struo.Api.GraphQl;

/// <summary>
/// Reads the startup-cached collection metadata and emits the full dynamic GraphQL schema:
/// one object type + list wrapper + filter input + two root query fields per collection, plus the
/// shared TagItem/Translation value types and the reusable scalar filter inputs.
/// </summary>
public sealed class StruoTypeModule(IMetadataProvider metadata, IEntityRegistry registry) : ITypeModule
{
#pragma warning disable CS0067 // collections are fixed at boot; never fires
    public event EventHandler<EventArgs>? TypesChanged;
#pragma warning restore CS0067

    public ValueTask<IReadOnlyCollection<ITypeSystemMember>> CreateTypesAsync(
        IDescriptorContext context, CancellationToken cancellationToken)
    {
        var builder = new CollectionSchemaBuilder(registry);
        var types = new List<ITypeSystemMember>();

        // Shared value types.
        types.Add(TagItemType());
        types.Add(TagItemInputType());
        types.Add(TranslationType());
        types.Add(DeletedFilterEnumType());
        types.Add(RevisionResolvers.RevisionType());
        types.AddRange(SharedFilterTypes.Build());

        var collections = metadata.GetCollections();
        foreach (var meta in collections)
            types.AddRange(builder.Build(meta));

        types.Add(BuildQueryExtension(collections.Select(c => c.Name)));
        types.Add(BuildMutationExtension(collections.Select(c => c.Name)));
        return new ValueTask<IReadOnlyCollection<ITypeSystemMember>>(types);
    }

    private static ObjectType TagItemType()
    {
        var config = new ObjectTypeConfiguration("TagItem", null, typeof(object));
        // TagItem is a Struo.Domain TagItem POCO {Value, Label?}; read via reflection-tolerant resolvers.
        config.Fields.Add(CollectionSchemaBuilder.Field("value", "String!", ctx => Prop(ctx.Parent<object>(), "Value")));
        config.Fields.Add(CollectionSchemaBuilder.Field("label", "String", ctx => Prop(ctx.Parent<object>(), "Label")));
        return ObjectType.CreateUnsafe(config);
    }

    private static InputObjectType TagItemInputType()
    {
        var config = new InputObjectTypeConfiguration(
            SchemaTypeMapper.TagItemInputName(), null, typeof(IReadOnlyDictionary<string, object?>));
        config.Fields.Add(new InputFieldConfiguration("value", null, TypeReference.Parse("String!")));
        config.Fields.Add(new InputFieldConfiguration("label", null, TypeReference.Parse("String")));
        return InputObjectType.CreateUnsafe(config);
    }

    // DeletedFilter (Phase 9b): SDL enum EXCLUDE/ONLY/WITH bound onto the C# Struo.Domain.Query
    // enum's own members (Exclude/Only/With) — the `deleted` list-query argument and the
    // ItemService/IGraphQlDataSource read path share this exact runtime type.
    private static EnumType DeletedFilterEnumType()
    {
        var config = new EnumTypeConfiguration("DeletedFilter", null, typeof(DeletedFilter));
        config.Values.Add(new EnumValueConfiguration("EXCLUDE", null, DeletedFilter.Exclude));
        config.Values.Add(new EnumValueConfiguration("ONLY", null, DeletedFilter.Only));
        config.Values.Add(new EnumValueConfiguration("WITH", null, DeletedFilter.With));
        return EnumType.CreateUnsafe(config);
    }

    private static ObjectType TranslationType()
    {
        var config = new ObjectTypeConfiguration("Translation", null, typeof(IReadOnlyDictionary<string, object?>));
        config.Fields.Add(CollectionSchemaBuilder.Field("locale", "String!",
            ctx => ctx.Parent<IReadOnlyDictionary<string, object?>>().GetValueOrDefault("locale")));
        config.Fields.Add(CollectionSchemaBuilder.Field("fields", "Any!",
            ctx => ctx.Parent<IReadOnlyDictionary<string, object?>>().GetValueOrDefault("fields")));
        return ObjectType.CreateUnsafe(config);
    }

    private ObjectTypeExtension BuildQueryExtension(IEnumerable<string> collectionNames)
    {
        var config = new ObjectTypeConfiguration("Query");
        foreach (var name in collectionNames)
        {
            config.Fields.Add(CollectionResolvers.SingleField(name, metadata));
            config.Fields.Add(CollectionResolvers.ListField(name, metadata));
            if (metadata.GetCollection(name)?.Revisions == true)
            {
                config.Fields.Add(RevisionResolvers.RevisionsField(name));
                config.Fields.Add(RevisionResolvers.RevisionField(name));
            }
        }
        return ObjectTypeExtension.CreateUnsafe(config);
    }

    private ObjectTypeExtension BuildMutationExtension(IEnumerable<string> collectionNames)
    {
        var config = new ObjectTypeConfiguration("Mutation");
        foreach (var name in collectionNames)
        {
            config.Fields.Add(MutationResolvers.CreateField(name, metadata));
            config.Fields.Add(MutationResolvers.UpdateField(name, metadata));
            config.Fields.Add(MutationResolvers.DeleteField(name));
            config.Fields.Add(MutationResolvers.RestoreField(name));
            if (metadata.GetCollection(name)?.Revisions == true)
                config.Fields.Add(MutationResolvers.RevertField(name));
        }
        return ObjectTypeExtension.CreateUnsafe(config);
    }

    private static object? Prop(object o, string name) =>
        o.GetType().GetProperty(name)?.GetValue(o);
}
