// src/Struo.Api/GraphQl/CollectionSchemaBuilder.cs
using System.Collections;
using HotChocolate.Resolvers;
using HotChocolate.Types;
using HotChocolate.Types.Descriptors;
using HotChocolate.Types.Descriptors.Configurations;
using Struo.Application.Metadata;
using Struo.Domain.Metadata.Enums;
using Struo.Domain.Metadata.Models;

namespace Struo.Api.GraphQl;

/// <summary>Builds all GraphQL types + root query fields for a single collection.</summary>
internal sealed class CollectionSchemaBuilder(
    IEntityRegistry registry, IMetadataProvider metadataProvider, IM2MDescriptorSource m2mSource)
{
    private static IReadOnlyDictionary<string, object?> ParentDict(IResolverContext ctx)
        => ctx.Parent<IReadOnlyDictionary<string, object?>>();

    // Repeater item sub-field resolvers must tolerate TWO parent shapes: a dictionary (the
    // hand-shaped test fixtures / anything future that projects Repeater rows as maps) AND a real
    // POCO (ItemService.Project emits a Repeater field's value as the entity's raw
    // List<TChild> of [CmsField] sub-POCOs, e.g. List<FaqItem> — never a list of dictionaries).
    // Mirrors StruoTypeModule.TagItemType, which already reads its POCO (TagItem) parent via
    // reflection rather than assuming a dictionary. Sub-field names are camelCase (GraphQL/SDL
    // convention) while CLR properties are PascalCase, so the POCO branch reads case-insensitively.
    private static object? ReadRepeaterSubField(object? parent, string name)
    {
        if (parent is IReadOnlyDictionary<string, object?> dict)
            return dict.GetValueOrDefault(name);
        if (parent is null) return null;
        return parent.GetType().GetProperty(name,
            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance
            | System.Reflection.BindingFlags.IgnoreCase)?.GetValue(parent);
    }

    internal IEnumerable<ITypeSystemMember> Build(CollectionMetadata meta)
    {
        var types = new List<ITypeSystemMember>();
        // M2M relations that carry junction payload — drives the additive <rel>Links/<Rel>Link/
        // <Rel>Junction/<Rel>LinkInput types below. Empty for every payload-free relation, so
        // nothing extra is generated for them (existing SDL stays byte-identical). Gated on
        // HasExposablePayload, not the raw HasPayload flag: HasPayload alone counts hidden/
        // unmappable fields (or a JunctionPayload whose junction collection metadata is missing
        // entirely), which would otherwise emit a zero-field <Rel>Junction — HotChocolate rejects
        // an object type with no fields at schema-build time ("has to at least define one field"),
        // taking the whole GraphQL endpoint down. A LinkInput alone would stay valid (it always
        // has `id: ID!`), but a Links input without a Links output is a half-feature, so the
        // entire additive set (read AND write) is skipped together for such a relation.
        var payloadRelations = m2mSource.M2MDescriptors(meta.Name)
            .Where(d => HasExposablePayload(d, metadataProvider))
            .ToList();

        types.Add(BuildObjectType(meta, types, payloadRelations)); // Article + nested repeater item types + <Rel>Link/<Rel>Junction types (added to `types`)
        types.Add(BuildListType(meta));                            // ArticleList { items, total }
        types.Add(BuildFilterInput(meta));                         // ArticleFilterInput

        // Repeater input item types — built ONCE per field, referenced by name in both inputs
        // (building them inside AddWritableFields would register a duplicate for create AND update).
        foreach (var f in meta.Fields)
            if (f is { Interface: FieldInterface.Repeater, Fields.Count: > 0 })
                types.Add(BuildRepeaterItemInputType(meta.Name, f));

        // Translation input types — built ONCE per collection (referenced by name in both inputs,
        // like the Repeater item input). Only for collections with a translation sidecar.
        if (meta.Translation is not null)
        {
            types.Add(BuildTranslationFieldsInputType(meta)); // XTranslationFieldsInput
            types.Add(BuildTranslationInputType(meta));        // XTranslationInput { locale, fields }
        }

        // <Rel>LinkInput types — built ONCE per M2M relation with payload, referenced by name from
        // both create/update inputs (same "build once" rule as the Repeater/Translation inputs above).
        foreach (var rel in payloadRelations)
            types.Add(BuildLinkInputType(meta, rel));

        types.Add(BuildCreateInput(meta, payloadRelations));       // ArticleCreateInput
        types.Add(BuildUpdateInput(meta, payloadRelations));       // ArticleUpdateInput
        return types;
    }

    private ObjectType BuildObjectType(
        CollectionMetadata meta, List<ITypeSystemMember> sink, IReadOnlyList<M2MDescriptor> payloadRelations)
    {
        var typeName = SchemaTypeMapper.TypeName(meta.Name);
        var desc = registry.Get(meta.Name);
        var config = new ObjectTypeConfiguration(typeName, null, typeof(IReadOnlyDictionary<string, object?>));

        // id + version (always present in the projection).
        config.Fields.Add(Field("id", "ID!", ctx => ParentDict(ctx).GetValueOrDefault("id")));
        config.Fields.Add(Field("version", "Long", ctx => ParentDict(ctx).GetValueOrDefault("version")));

        foreach (var f in meta.Fields)
        {
            if (f.Hidden || SchemaTypeMapper.IsExcluded(f.Interface)) continue;

            switch (f.Interface)
            {
                case FieldInterface.Tags:
                    config.Fields.Add(Field(f.Name, "[TagItem!]", ctx => ParentDict(ctx).GetValueOrDefault(f.Name)));
                    break;
                case FieldInterface.Repeater when f.Fields is { Count: > 0 }:
                    var itemType = BuildRepeaterItemType(meta.Name, f);
                    sink.Add(itemType);
                    config.Fields.Add(Field(f.Name, $"[{SchemaTypeMapper.RepeaterItemTypeName(meta.Name, f.Name)}!]",
                        ctx => ParentDict(ctx).GetValueOrDefault(f.Name)));
                    break;
                case FieldInterface.File or FieldInterface.Image:
                    config.Fields.Add(Field(f.Name, "ID", ctx => ParentDict(ctx).GetValueOrDefault(f.Name)));
                    config.Fields.Add(FileFieldResolvers.ScalarFileField(f.Name)); // "<name-stripId>": File
                    break;
                case FieldInterface.Files:
                    config.Fields.Add(Field(f.Name, "[ID!]", ctx => ParentDict(ctx).GetValueOrDefault(f.Name)));
                    config.Fields.Add(FileFieldResolvers.ListFileField(f.Name)); // "<name>Files": [File!]
                    break;
                default:
                    var clr = ClrType(desc, f.Name);
                    var sdl = SchemaTypeMapper.ScalarSdl(f.Interface, clr);
                    if (sdl is null) break;
                    config.Fields.Add(Field(f.Name, sdl, ctx => ParentDict(ctx).GetValueOrDefault(f.Name)));
                    break;
            }
        }

        // relations (single-level value pre-nested by ItemService deep expansion).
        // To-many (O2M/M2M) list fields gain nested-list args; M2O stays a bare object field.
        foreach (var rel in meta.Relations)
            AddRelationField(config, sink, meta, rel, payloadRelations);

        // translations map (always present in projection when the collection has a sidecar).
        if (meta.Translation is not null)
            config.Fields.Add(Field("translations", "[Translation!]", ctx => TranslationList(ParentDict(ctx))));

        return ObjectType.CreateUnsafe(config);
    }

    // Adds a single relation's read-side field(s) to the collection's object type: a bare object
    // field for ManyToOne, or a filterable/sortable list field for OneToMany/ManyToMany — plus,
    // for a ManyToMany relation whose junction carries an exposable payload, the additive
    // `<rel>Links: [<Rel>Link!]` field (see BuildObjectType's caller comment for why this is
    // additive rather than a replacement of the bare `<rel>` field).
    private void AddRelationField(
        ObjectTypeConfiguration config, List<ITypeSystemMember> sink, CollectionMetadata meta,
        RelationMetadata rel, IReadOnlyList<M2MDescriptor> payloadRelations)
    {
        var target = SchemaTypeMapper.TypeName(rel.TargetCollection);
        if (rel.Kind == RelationKind.ManyToOne)
        {
            config.Fields.Add(Field(rel.Name, target, ctx => ParentDict(ctx).GetValueOrDefault(rel.Name)));
            return;
        }

        var field = Field(rel.Name, $"[{target}!]", ctx => ParentDict(ctx).GetValueOrDefault(rel.Name));
        field.Arguments.Add(new ArgumentConfiguration("filter", null, TypeReference.Parse($"{target}FilterInput")));
        field.Arguments.Add(new ArgumentConfiguration("sort", null, TypeReference.Parse("[String!]")));
        field.Arguments.Add(new ArgumentConfiguration("limit", null, TypeReference.Parse("Int")));
        field.Arguments.Add(new ArgumentConfiguration("offset", null, TypeReference.Parse("Int")));
        config.Fields.Add(field);

        // M2M relation carrying junction payload -> additive `<rel>Links: [<Rel>Link!]`
        // field reading the SAME underlying list as `<rel>` above (untouched); each element
        // is exposed as { node, junction } instead of the bare target.
        if (rel.Kind != RelationKind.ManyToMany) return;

        var descriptor = DescriptorFor(payloadRelations, rel.Name);
        if (descriptor is null) return;

        sink.Add(BuildJunctionType(meta, descriptor));
        sink.Add(BuildLinkType(meta, target, descriptor));
        config.Fields.Add(Field(
            SchemaTypeMapper.LinksFieldName(rel.Name),
            $"[{SchemaTypeMapper.LinkTypeName(meta.Name, rel.Name)}!]",
            ctx => ParentDict(ctx).GetValueOrDefault(rel.Name)));
    }

    private static ObjectType BuildRepeaterItemType(string collection, FieldMetadata repeater)
    {
        // RuntimeType = object (not IReadOnlyDictionary<string,object?>): the real parent at
        // execution time is a POCO (see ReadRepeaterSubField above), mirroring TagItemType.
        var config = new ObjectTypeConfiguration(
            SchemaTypeMapper.RepeaterItemTypeName(collection, repeater.Name), null, typeof(object));
        foreach (var sub in repeater.Fields!)
        {
            if (sub.Hidden || SchemaTypeMapper.IsExcluded(sub.Interface)) continue;
            var sdl = SchemaTypeMapper.ScalarSdl(sub.Interface, typeof(string)); // lean scalar sub-field set
            if (sdl is null) continue;
            config.Fields.Add(Field(sub.Name, sdl, ctx => ReadRepeaterSubField(ctx.Parent<object>(), sub.Name)));
        }
        return ObjectType.CreateUnsafe(config);
    }

    private InputObjectType BuildRepeaterItemInputType(string collection, FieldMetadata repeater)
    {
        var config = new InputObjectTypeConfiguration(
            SchemaTypeMapper.RepeaterItemInputTypeName(collection, repeater.Name), null,
            typeof(IReadOnlyDictionary<string, object?>));
        foreach (var sub in repeater.Fields!)
        {
            if (sub.Hidden || sub.ReadOnly || sub.IsSystem) continue;
            // Lean scalar sub-field set; input hint is string (same as the read side's BuildRepeaterItemType).
            var sdl = SchemaTypeMapper.WritableInputSdl(sub.Interface, typeof(string));
            if (sdl is null) continue;
            config.Fields.Add(new InputFieldConfiguration(sub.Name, null, TypeReference.Parse(sdl)));
        }
        return InputObjectType.CreateUnsafe(config);
    }

    // <Parent><Rel>Junction: one field per non-hidden payload field of a payload-carrying M2M
    // relation. RuntimeType = dict — matches the "_junction" projection ItemService's deep
    // expansion attaches to each target element when the relation carries payload.
    private ObjectType BuildJunctionType(CollectionMetadata meta, M2MDescriptor descriptor)
    {
        var config = new ObjectTypeConfiguration(
            SchemaTypeMapper.JunctionTypeName(meta.Name, descriptor.RelationName), null,
            typeof(IReadOnlyDictionary<string, object?>));
        foreach (var (pf, _, sdl) in ExposableJunctionFields(descriptor, metadataProvider))
            config.Fields.Add(Field(pf.Name, sdl, ctx => ParentDict(ctx).GetValueOrDefault(pf.Name)));
        return ObjectType.CreateUnsafe(config);
    }

    // Single source of truth for "does this M2M relation's junction have at least one field the
    // schema can actually declare" — used by Build's payloadRelations gate AND (so the two can
    // never drift apart) by MutationResolvers.PayloadRelations, which needs the identical decision
    // to know which relations' `<rel>Links` input key to fold. `descriptor.HasPayload` is checked
    // first: JunctionPayload is null for a payload-free relation, and ExposableJunctionFields
    // dereferences it unconditionally.
    internal static bool HasExposablePayload(M2MDescriptor descriptor, IMetadataProvider metadataProvider) =>
        descriptor.HasPayload && ExposableJunctionFields(descriptor, metadataProvider).Any();

    // Enumerates the payload fields BuildJunctionType actually declares: non-hidden, resolvable
    // against the junction collection's own metadata (a non-null, non-excluded FieldInterface —
    // null when JunctionCollection is unset or metadataProvider.GetCollection(...) can't find it),
    // and mappable via ScalarSdl. Computed once and reused both to gate whether the whole
    // <Rel>Link/<Rel>Junction/<rel>Links/<Rel>LinkInput set is generated for a relation at all
    // (see HasExposablePayload above — HotChocolate rejects a zero-field object type at
    // schema-build time) and to build <Rel>Junction's actual field list, so the two can never
    // disagree.
    private static IEnumerable<(JunctionPayloadField Field, FieldInterface Interface, string Sdl)> ExposableJunctionFields(
        M2MDescriptor descriptor, IMetadataProvider metadataProvider)
    {
        foreach (var pf in descriptor.JunctionPayload!)
        {
            if (pf.Hidden) continue;
            var iface = JunctionFieldInterface(descriptor, pf.Name, metadataProvider);
            if (iface is null || SchemaTypeMapper.IsExcluded(iface.Value)) continue;
            var clr = descriptor.JunctionType.GetProperty(pf.Property)?.PropertyType;
            var sdl = SchemaTypeMapper.ScalarSdl(iface.Value, clr);
            if (sdl is null) continue;
            yield return (pf, iface.Value, sdl);
        }
    }

    // Shared lookup used by both the read (BuildObjectType) and write (AddWritableFields) relation
    // loops to find a relation's payload descriptor, if any, among the collection's already-filtered
    // payloadRelations (see Build).
    private static M2MDescriptor? DescriptorFor(IReadOnlyList<M2MDescriptor> payloadRelations, string relationName) =>
        payloadRelations.FirstOrDefault(d => string.Equals(d.RelationName, relationName, StringComparison.OrdinalIgnoreCase));

    // Builds the "<Parent><Rel>Link" wrapper type for M2M relations that carry junction payload,
    // exposing a "node" field (the target row) and a "junction" field (the junction payload) for
    // one entry per element of the underlying "<rel>" list. `node` is the element dict itself
    // (already a full target row, exactly what the bare `<rel>` field's own sub-resolvers read via
    // ParentDict); `junction` reads the "_junction" key the deep expansion attaches, or null when
    // the caller lacks the junction read grant — hence the nullable (non-required) return type.
    private static ObjectType BuildLinkType(CollectionMetadata meta, string targetTypeName, M2MDescriptor descriptor)
    {
        var config = new ObjectTypeConfiguration(
            SchemaTypeMapper.LinkTypeName(meta.Name, descriptor.RelationName), null,
            typeof(IReadOnlyDictionary<string, object?>));
        config.Fields.Add(Field("node", $"{targetTypeName}!", ctx => ParentDict(ctx)));
        config.Fields.Add(Field("junction", SchemaTypeMapper.JunctionTypeName(meta.Name, descriptor.RelationName),
            ctx => ParentDict(ctx).GetValueOrDefault("_junction")));
        return ObjectType.CreateUnsafe(config);
    }

    // <Parent><Rel>LinkInput: { id: ID!, <writable payload fields> }. JunctionPayload already
    // excludes ReadOnly/IsSystem columns (see RelationshipGraph.JunctionPayloadOf) — only Hidden
    // remains to filter here, mirroring AddWritableFields' writability rule.
    private InputObjectType BuildLinkInputType(CollectionMetadata meta, M2MDescriptor descriptor)
    {
        var config = new InputObjectTypeConfiguration(
            SchemaTypeMapper.LinkInputName(meta.Name, descriptor.RelationName), null,
            typeof(IReadOnlyDictionary<string, object?>));
        config.Fields.Add(new InputFieldConfiguration("id", null, TypeReference.Parse("ID!")));
        foreach (var pf in descriptor.JunctionPayload!)
        {
            if (pf.Hidden) continue;
            var iface = JunctionFieldInterface(descriptor, pf.Name, metadataProvider);
            if (iface is null) continue;
            var clr = descriptor.JunctionType.GetProperty(pf.Property)?.PropertyType;
            var sdl = SchemaTypeMapper.WritableInputSdl(iface.Value, clr);
            if (sdl is null) continue;
            config.Fields.Add(new InputFieldConfiguration(pf.Name, null, TypeReference.Parse(sdl)));
        }
        return InputObjectType.CreateUnsafe(config);
    }

    // Resolves a junction payload field's FieldInterface from the junction collection's own
    // [CmsCollection] metadata — JunctionPayloadField itself carries only Name/Property/Hidden.
    private static FieldInterface? JunctionFieldInterface(
        M2MDescriptor descriptor, string payloadFieldName, IMetadataProvider metadataProvider)
    {
        if (descriptor.JunctionCollection is not { } junctionCollection) return null;
        var junctionMeta = metadataProvider.GetCollection(junctionCollection);
        return junctionMeta?.Fields.FirstOrDefault(f =>
            string.Equals(f.Name, payloadFieldName, StringComparison.OrdinalIgnoreCase))?.Interface;
    }

    // XTranslationFieldsInput: one input field per translatable own-field, all nullable, SDL via the
    // shared WritableInputSdl (so a translatable Image/File -> ID, RichText/Text/Textarea -> String).
    private InputObjectType BuildTranslationFieldsInputType(CollectionMetadata meta)
    {
        var desc = registry.Get(meta.Name);
        var config = new InputObjectTypeConfiguration(
            SchemaTypeMapper.TranslationFieldsInputName(meta.Name), null,
            typeof(IReadOnlyDictionary<string, object?>));
        foreach (var f in meta.Fields)
        {
            if (!f.Translatable) continue;
            if (f.Hidden || f.ReadOnly || f.IsSystem) continue;
            var sdl = SchemaTypeMapper.WritableInputSdl(f.Interface, ClrType(desc, f.Name));
            if (sdl is null) continue;
            config.Fields.Add(new InputFieldConfiguration(f.Name, null, TypeReference.Parse(sdl)));
        }
        return InputObjectType.CreateUnsafe(config);
    }

    // XTranslationInput: one entry = { locale: String!, fields: XTranslationFieldsInput! }.
    private static InputObjectType BuildTranslationInputType(CollectionMetadata meta)
    {
        var config = new InputObjectTypeConfiguration(
            SchemaTypeMapper.TranslationInputName(meta.Name), null,
            typeof(IReadOnlyDictionary<string, object?>));
        config.Fields.Add(new InputFieldConfiguration("locale", null, TypeReference.Parse("String!")));
        config.Fields.Add(new InputFieldConfiguration(
            "fields", null, TypeReference.Parse(SchemaTypeMapper.TranslationFieldsInputName(meta.Name) + "!")));
        return InputObjectType.CreateUnsafe(config);
    }

    private ObjectType BuildListType(CollectionMetadata meta)
    {
        var config = new ObjectTypeConfiguration(SchemaTypeMapper.TypeName(meta.Name) + "List", null, typeof(PagedResultView));
        config.Fields.Add(Field("items", $"[{SchemaTypeMapper.TypeName(meta.Name)}!]!",
            ctx => ctx.Parent<PagedResultView>().Items));
        config.Fields.Add(Field("total", "Int!", ctx => ctx.Parent<PagedResultView>().Total));
        return ObjectType.CreateUnsafe(config);
    }

    private InputObjectType BuildFilterInput(CollectionMetadata meta)
    {
        var name = SchemaTypeMapper.TypeName(meta.Name) + "FilterInput";
        var desc = registry.Get(meta.Name);
        var config = new InputObjectTypeConfiguration(name, null, typeof(IReadOnlyDictionary<string, object?>));
        config.Fields.Add(new InputFieldConfiguration("and", null, TypeReference.Parse($"[{name}!]")));
        config.Fields.Add(new InputFieldConfiguration("or", null, TypeReference.Parse($"[{name}!]")));
        config.Fields.Add(new InputFieldConfiguration("id", null, TypeReference.Parse("IdFilter")));

        foreach (var f in meta.Fields)
        {
            if (f.Hidden || SchemaTypeMapper.IsExcluded(f.Interface)) continue;
            if (!IsFilterable(f.Interface)) continue;
            var opInput = FilterInputTranslator.OperatorInputTypeName(f.Interface, ClrType(desc, f.Name));
            config.Fields.Add(new InputFieldConfiguration(f.Name, null, TypeReference.Parse(opInput)));
        }
        // Cross-relation filter inputs. M2O contributes its FK column as a filterable IdFilter
        // (parity with the REST allowlist) PLUS a nested target FilterInput. O2M/M2M carry no FK
        // on this collection, so they contribute only the nested target FilterInput (ANY/EXISTS
        // via RelationFilterResolver's to-many hops). Referenced by name -> recursive /
        // self-referential / cyclic input types resolve like and/or (no build loop). Flattened to
        // a dotted FieldPath by FilterInputTranslator.
        foreach (var rel in meta.Relations)
        {
            string? targetFilter = null;
            if (rel.Kind == RelationKind.ManyToOne && rel.ForeignKey is { } fk)
            {
                config.Fields.Add(new InputFieldConfiguration(fk, null, TypeReference.Parse("IdFilter")));
                targetFilter = SchemaTypeMapper.TypeName(rel.TargetCollection) + "FilterInput";
            }
            else if (rel.Kind is RelationKind.OneToMany or RelationKind.ManyToMany)
            {
                targetFilter = SchemaTypeMapper.TypeName(rel.TargetCollection) + "FilterInput";
            }

            if (targetFilter is not null)
                config.Fields.Add(new InputFieldConfiguration(rel.Name, null, TypeReference.Parse(targetFilter)));
        }

        return InputObjectType.CreateUnsafe(config);
    }

    private InputObjectType BuildCreateInput(CollectionMetadata meta, IReadOnlyList<M2MDescriptor> payloadRelations)
    {
        var config = new InputObjectTypeConfiguration(
            SchemaTypeMapper.CreateInputName(meta.Name), null, typeof(IReadOnlyDictionary<string, object?>));
        AddWritableFields(config, meta, payloadRelations);
        return InputObjectType.CreateUnsafe(config);
    }

    private InputObjectType BuildUpdateInput(CollectionMetadata meta, IReadOnlyList<M2MDescriptor> payloadRelations)
    {
        var config = new InputObjectTypeConfiguration(
            SchemaTypeMapper.UpdateInputName(meta.Name), null, typeof(IReadOnlyDictionary<string, object?>));
        // Optimistic-concurrency token (update-only). Absent -> no protection (backward compatible).
        config.Fields.Add(new InputFieldConfiguration("version", null, TypeReference.Parse("Long")));
        AddWritableFields(config, meta, payloadRelations);
        return InputObjectType.CreateUnsafe(config);
    }

    // Shared by create/update inputs: writable own-fields (scalars, File/Image, Files, multi-value,
    // Json/KeyValue), Tags, Repeater, and M2M foreign-key arrays — all nullable. Translatable own-fields
    // are excluded — translations use the dedicated typed translations input instead.
    private void AddWritableFields(
        InputObjectTypeConfiguration config, CollectionMetadata meta, IReadOnlyList<M2MDescriptor> payloadRelations)
    {
        var desc = registry.Get(meta.Name);
        AddWritableOwnFields(config, meta, desc);
        AddWritableRelationFields(config, meta, payloadRelations);

        // Typed translations input — only when the collection has a translation sidecar.
        // Translatable own-fields stay excluded above (`if (f.Translatable) continue;`); they are
        // carried here instead.
        if (meta.Translation is not null)
            config.Fields.Add(new InputFieldConfiguration(
                "translations", null,
                TypeReference.Parse($"[{SchemaTypeMapper.TranslationInputName(meta.Name)}!]")));
    }

    private static void AddWritableOwnFields(InputObjectTypeConfiguration config, CollectionMetadata meta, EntityDescriptor? desc)
    {
        foreach (var f in meta.Fields)
        {
            if (f.Hidden || f.ReadOnly || f.IsSystem) continue;
            if (f.Translatable) continue; // handled by the typed translations input below

            string? sdl = f.Interface switch
            {
                FieldInterface.Tags => $"[{SchemaTypeMapper.TagItemInputName()}!]",
                FieldInterface.Repeater when f.Fields is { Count: > 0 } =>
                    $"[{SchemaTypeMapper.RepeaterItemInputTypeName(meta.Name, f.Name)}!]",
                _ => SchemaTypeMapper.WritableInputSdl(f.Interface, ClrType(desc, f.Name)),
            };
            if (sdl is null) continue;
            config.Fields.Add(new InputFieldConfiguration(f.Name, null, TypeReference.Parse(sdl)));
        }
    }

    // M2O foreign keys (as ID) + M2M relations (as [ID!] target-id arrays).
    private static void AddWritableRelationFields(
        InputObjectTypeConfiguration config, CollectionMetadata meta, IReadOnlyList<M2MDescriptor> payloadRelations)
    {
        foreach (var rel in meta.Relations)
        {
            if (rel.Kind == RelationKind.ManyToOne && rel.ForeignKey is { } fk)
                config.Fields.Add(new InputFieldConfiguration(fk, null, TypeReference.Parse("ID")));
            else if (rel.Kind == RelationKind.ManyToMany)
            {
                config.Fields.Add(new InputFieldConfiguration(rel.Name, null, TypeReference.Parse("[ID!]")));

                // M2M relation carrying junction payload -> additive `<rel>Links: [<Rel>LinkInput!]`
                // alongside the plain `[ID!]` field above (untouched).
                var descriptor = DescriptorFor(payloadRelations, rel.Name);
                if (descriptor is not null)
                    config.Fields.Add(new InputFieldConfiguration(
                        SchemaTypeMapper.LinksFieldName(rel.Name), null,
                        TypeReference.Parse($"[{SchemaTypeMapper.LinkInputName(meta.Name, rel.Name)}!]")));
            }
        }
    }

    // Filterable own-field interfaces: scalars only (parity with REST; multi-value/json/kv/files/repeater excluded).
    private static bool IsFilterable(FieldInterface i) => i is
        FieldInterface.Text or FieldInterface.Textarea or FieldInterface.Slug or FieldInterface.Email
        or FieldInterface.Url or FieldInterface.Color or FieldInterface.Phone or FieldInterface.Select
        or FieldInterface.Radio or FieldInterface.Number or FieldInterface.Slider or FieldInterface.Rating
        or FieldInterface.Boolean or FieldInterface.Checkbox or FieldInterface.Date or FieldInterface.DateTime
        or FieldInterface.Time or FieldInterface.Uuid;

    // MUST use TryGetValue (never the indexer): the fixtures deliberately omit translatable fields
    // (e.g. "title") from FieldToProperty, mirroring the real scanner — an indexer lookup would throw.
    private static Type? ClrType(EntityDescriptor? desc, string fieldName)
    {
        if (desc is null) return null;
        return desc.FieldToProperty.TryGetValue(fieldName, out var prop)
            ? desc.EntityType.GetProperty(prop)?.PropertyType
            : null;
    }

    private static IReadOnlyList<object> TranslationList(IReadOnlyDictionary<string, object?> parent)
    {
        // ItemService.OverlayTranslationsAsync projects translations as the CONCRETE type
        // Dictionary<string, Dictionary<string, object?>> — which does not implement the generic
        // IDictionary<string, object?> (generic dictionary interfaces are not covariant in the
        // value type in .NET), so a guard written against that generic interface always misses and
        // silently returns []. Guard on the non-generic System.Collections.IDictionary instead —
        // it is implemented by any Dictionary<TKey,TValue> regardless of TValue, and still lets us
        // return [] when translations is genuinely absent/null.
        if (parent.GetValueOrDefault("translations") is not IDictionary map) return [];
        var result = new List<object>(map.Count);
        foreach (DictionaryEntry entry in map)
        {
            result.Add(new Dictionary<string, object?>
            {
                ["locale"] = entry.Key,
                ["fields"] = entry.Value
            });
        }
        return result;
    }

    internal static ObjectFieldConfiguration Field(string name, string sdl, PureFieldDelegate pure)
        => new(name, null, TypeReference.Parse(sdl), pureResolver: pure);
}

/// <summary>Adapter so the ArticleList type reads items/total from the Application PagedResult.</summary>
internal sealed record PagedResultView(IReadOnlyList<object> Items, int Total);
