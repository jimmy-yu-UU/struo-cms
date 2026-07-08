// src/Struo.Api/GraphQl/StruoTypeModule.cs
using HotChocolate.Execution.Configuration;
using HotChocolate.Types;
using HotChocolate.Types.Descriptors;
using HotChocolate.Types.Descriptors.Configurations;
using Struo.Application.Metadata;

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
        types.Add(TranslationType());
        types.AddRange(SharedFilterTypes.Build());

        var collections = metadata.GetCollections();
        foreach (var meta in collections)
            types.AddRange(builder.Build(meta));

        types.Add(BuildQueryExtension(collections.Select(c => c.Name)));
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
        }
        return ObjectTypeExtension.CreateUnsafe(config);
    }

    private static object? Prop(object o, string name) =>
        o.GetType().GetProperty(name)?.GetValue(o);
}
