// src/Struo.Api/GraphQl/StruoTypeModule.cs
using HotChocolate.Execution.Configuration;
using HotChocolate.Types;
using HotChocolate.Types.Descriptors;
using HotChocolate.Types.Descriptors.Configurations;
using Struo.Application.Metadata;
using Struo.Domain.Metadata.Models;
using Struo.Domain.Query;

namespace Struo.Api.GraphQl;

/// <summary>
/// Reads the startup-cached collection metadata and emits the full dynamic GraphQL schema:
/// one object type + list wrapper + filter input + two root query fields per collection, plus the
/// shared TagItem/Translation value types and the reusable scalar filter inputs.
/// </summary>
public sealed class StruoTypeModule(
    IMetadataProvider metadata, IEntityRegistry registry, IM2MDescriptorSource m2mSource) : ITypeModule
{
#pragma warning disable CS0067 // collections are fixed at boot; never fires
    public event EventHandler<EventArgs>? TypesChanged;
#pragma warning restore CS0067

    public ValueTask<IReadOnlyCollection<ITypeSystemMember>> CreateTypesAsync(
        IDescriptorContext context, CancellationToken cancellationToken)
    {
        var types = new List<ITypeSystemMember>();

        // Shared value types.
        types.Add(TagItemType());
        types.Add(TagItemInputType());
        types.Add(TranslationType());
        types.Add(DeletedFilterEnumType());
        types.Add(RevisionResolvers.RevisionType());
        types.AddRange(SharedFilterTypes.Build());
        types.AddRange(SharedFacetTypes.Build());

        var collections = BuildCollections();
        GraphQlNameValidator.Validate(collections.Select(c => c.Surface).ToList());
        foreach (var collection in collections)
            types.AddRange(collection.Types);

        types.Add(ExtensionOf("Query", collections.SelectMany(c => c.QueryFields)));
        types.Add(MutationExtension(collections.SelectMany(c => c.MutationFields)));
        return new ValueTask<IReadOnlyCollection<ITypeSystemMember>>(types);
    }

    /// <summary>
    /// Fails fast with a <see cref="Struo.Domain.Metadata.MetadataException"/> naming the colliding
    /// collection(s) and GraphQL name(s) when the collection set would generate duplicate names.
    /// <see cref="CreateTypesAsync"/> runs the same check on the types it is about to register.
    /// </summary>
    internal void ValidateNames() => GraphQlNameValidator.Validate(DescribeCollections());

    internal IReadOnlyList<CollectionSurface> DescribeCollections() =>
        BuildCollections().Select(c => c.Surface).ToList();

    private sealed record CollectionTypes(
        IReadOnlyList<ITypeSystemMember> Types,
        IReadOnlyList<ObjectFieldConfiguration> QueryFields,
        IReadOnlyList<ObjectFieldConfiguration> MutationFields,
        CollectionSurface Surface);

    private List<CollectionTypes> BuildCollections()
    {
        var builder = new CollectionSchemaBuilder(registry, metadata, m2mSource);
        var result = new List<CollectionTypes>();
        foreach (var meta in metadata.GetCollections())
        {
            var built = builder.Build(meta);
            var query = QueryFieldsOf(meta);
            var mutation = MutationFieldsOf(meta);
            result.Add(new CollectionTypes(
                built.Types, query, mutation,
                new CollectionSurface(
                    meta.Name,
                    built.TypeNames,
                    query.Select(f => f.Name).ToList(),
                    mutation.Select(f => f.Name).ToList())));
        }
        return result;
    }

    private List<ObjectFieldConfiguration> QueryFieldsOf(CollectionMetadata meta)
    {
        var fields = new List<ObjectFieldConfiguration>
        {
            CollectionResolvers.SingleField(meta.Name, metadata),
            CollectionResolvers.ListField(meta.Name, metadata),
        };
        if (meta.Revisions)
        {
            fields.Add(RevisionResolvers.RevisionsField(meta.Name));
            fields.Add(RevisionResolvers.RevisionField(meta.Name));
        }
        return fields;
    }

    private List<ObjectFieldConfiguration> MutationFieldsOf(CollectionMetadata meta)
    {
        var fields = new List<ObjectFieldConfiguration>
        {
            MutationResolvers.CreateField(meta.Name, metadata),
            MutationResolvers.UpdateField(meta.Name, metadata),
            MutationResolvers.DeleteField(meta.Name),
            MutationResolvers.RestoreField(meta.Name),
        };
        if (meta.Revisions) fields.Add(MutationResolvers.RevertField(meta.Name));
        return fields;
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
            SchemaTypeMapper.TagItemInputName, null, typeof(IReadOnlyDictionary<string, object?>));
        config.Fields.Add(new InputFieldConfiguration("value", null, TypeReference.Parse("String!")));
        config.Fields.Add(new InputFieldConfiguration("label", null, TypeReference.Parse("String")));
        return InputObjectType.CreateUnsafe(config);
    }

    // DeletedFilter: SDL enum EXCLUDE/ONLY/WITH bound onto the C# Struo.Domain.Query
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

    private static ObjectTypeExtension ExtensionOf(string rootName, IEnumerable<ObjectFieldConfiguration> fields)
    {
        var config = new ObjectTypeConfiguration(rootName);
        foreach (var field in fields) config.Fields.Add(field);
        return ObjectTypeExtension.CreateUnsafe(config);
    }

    private static ObjectTypeExtension MutationExtension(IEnumerable<ObjectFieldConfiguration> fields)
    {
        var config = new ObjectTypeConfiguration("Mutation");
        foreach (var field in fields)
        {
            field.MiddlewareConfigurations.Add(MutationAuthentication.Configuration);
            config.Fields.Add(field);
        }
        return ObjectTypeExtension.CreateUnsafe(config);
    }

    private static object? Prop(object o, string name) =>
        o.GetType().GetProperty(name)?.GetValue(o);
}
