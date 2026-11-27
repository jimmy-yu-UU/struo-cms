// src/Struo.Api/GraphQl/CollectionSchemaBuilder.cs
using System.Collections;
using HotChocolate.Resolvers;
using HotChocolate.Types;
using HotChocolate.Types.Descriptors;
using HotChocolate.Types.Descriptors.Configurations;
using Struo.Application.Metadata;
using Struo.Domain.Metadata.Enums;
using Struo.Domain.Metadata.Models;
using Struo.Domain.Query;

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

    /// <summary>A collection's built types, with the name each was created under.</summary>
    internal sealed record BuiltCollection(IReadOnlyList<ITypeSystemMember> Types, IReadOnlyList<string> TypeNames);

    // Every type is created through NewObject/NewInput, which record the type's name here: a type
    // built with CreateUnsafe has no name until HotChocolate initialises it, so the names the
    // startup collision check sees are collected at creation instead.
    private readonly List<string> _createdNames = [];

    // Every field of every type goes in through _declared, and NewObject/NewInput hand the finished type to it,
    // so a field name declared twice stops the host with a message about the two declarations.
    private readonly DeclaredFields _declared = new();

    private ObjectType NewObject(ObjectTypeConfiguration config)
    {
        _createdNames.Add(config.Name);
        _declared.Inspect(config);
        return ObjectType.CreateUnsafe(config);
    }

    private InputObjectType NewInput(InputObjectTypeConfiguration config)
    {
        _createdNames.Add(config.Name);
        _declared.Inspect(config);
        return InputObjectType.CreateUnsafe(config);
    }

    internal BuiltCollection Build(CollectionMetadata meta)
    {
        _createdNames.Clear();
        _declared.Clear();
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
        var payloadRelations = PayloadRelations(meta);

        types.Add(BuildObjectType(meta, types, payloadRelations)); // Article + nested repeater item types + <Rel>Link/<Rel>Junction types (added to `types`)
        types.Add(BuildListType(meta));                            // ArticleList { items, total }
        types.Add(BuildFilterInput(meta, payloadRelations));       // ArticleFilterInput

        // Relation-specific filter input pair, one per M2M relation carrying an exposable payload
        // (parity with payloadRelations' Link/Junction pair above): <Parent><Rel>FilterInput (the
        // target's filterable fields + and/or/some/none of itself + junction: <Parent><Rel>JunctionFilterInput)
        // and <Parent><Rel>JunctionFilterInput itself. The junction filter input (and the parent
        // relation filter input's "junction" field) is skipped entirely when the relation's payload
        // has no filterable field — HotChocolate rejects a zero-field input type exactly like it
        // rejects a zero-field object type (see HasExposablePayload's comment above).
        foreach (var rel in payloadRelations)
        {
            var junctionFilterInput = BuildJunctionFilterInput(meta, rel);
            types.Add(BuildRelationFilterInput(meta, rel, junctionFilterInput is not null));
            if (junctionFilterInput is not null) types.Add(junctionFilterInput);
        }

        // Repeater input item types — built ONCE per field, referenced by name in both inputs
        // (building them inside AddWritableFields would register a duplicate for create AND update).
        foreach (var f in meta.Fields.Where(f => f is { Interface: FieldInterface.Repeater, Fields.Count: > 0 }))
            types.Add(BuildRepeaterItemInputType(meta, f));

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
        _declared.Require(meta.Name);
        return new BuiltCollection(types, _createdNames.ToList());
    }

    /// <summary>Stops startup with a message about the collection when its create input type would have no field.</summary>
    internal void RequireWritableFields(CollectionMetadata meta) =>
        WritableInput.RequireAny(meta, registry.Get(meta.Name), PayloadRelations(meta));

    private List<M2MDescriptor> PayloadRelations(CollectionMetadata meta) =>
        m2mSource.M2MDescriptors(meta.Name)
            .Where(d => HasExposablePayload(d, metadataProvider))
            .ToList();

    private ObjectType BuildObjectType(
        CollectionMetadata meta, List<ITypeSystemMember> sink, IReadOnlyList<M2MDescriptor> payloadRelations)
    {
        var typeName = SchemaTypeMapper.TypeName(meta.Name);
        var desc = registry.Get(meta.Name);
        var config = new ObjectTypeConfiguration(typeName, null, typeof(IReadOnlyDictionary<string, object?>));

        // id + version (always present in the projection).
        _declared.Add(config, Field("id", "ID!", ctx => ParentDict(ctx).GetValueOrDefault("id")));
        _declared.Add(config, Field("version", "Long", ctx => ParentDict(ctx).GetValueOrDefault("version")));

        foreach (var f in meta.Fields)
        {
            if (f.Hidden || SchemaTypeMapper.IsExcluded(f.Interface)) continue;

            var origin = FieldOrigin.Field(meta, desc, f);
            switch (f.Interface)
            {
                case FieldInterface.Tags:
                    _declared.Add(config, Field(f.Name, "[TagItem!]", ctx => ParentDict(ctx).GetValueOrDefault(f.Name)), origin);
                    break;
                case FieldInterface.Repeater when f.Fields is { Count: > 0 }:
                    var itemType = BuildRepeaterItemType(meta, f);
                    sink.Add(itemType);
                    _declared.Add(config, Field(f.Name, $"[{SchemaTypeMapper.RepeaterItemTypeName(meta.Name, f.Name)}!]",
                        ctx => ParentDict(ctx).GetValueOrDefault(f.Name)), origin);
                    break;
                case FieldInterface.File or FieldInterface.Image:
                    _declared.Add(config, Field(f.Name, "ID", ctx => ParentDict(ctx).GetValueOrDefault(f.Name)), origin);
                    var fileObject = FileFieldResolvers.ScalarFileField(f.Name); // "<name-stripId>": File
                    _declared.Add(config, fileObject, FieldOrigin.FileObject(meta, desc, f, fileObject.Name));
                    break;
                case FieldInterface.Files:
                    _declared.Add(config, Field(f.Name, "[ID!]", ctx => ParentDict(ctx).GetValueOrDefault(f.Name)), origin);
                    var fileList = FileFieldResolvers.ListFileField(f.Name); // "<name>Files": [File!]
                    _declared.Add(config, fileList, FieldOrigin.FileObject(meta, desc, f, fileList.Name));
                    break;
                default:
                    var clr = ClrType(desc, f.Name);
                    var sdl = SchemaTypeMapper.ScalarSdl(f.Interface, clr);
                    if (sdl is null) break;
                    _declared.Add(config, Field(f.Name, sdl, ctx => ParentDict(ctx).GetValueOrDefault(f.Name)), origin);
                    break;
            }
        }

        // relations (single-level value pre-nested by ItemService deep expansion).
        // To-many (O2M/M2M) list fields gain nested-list args; M2O stays a bare object field.
        foreach (var rel in meta.Relations)
            AddRelationField(config, sink, meta, rel, payloadRelations);

        // translations map (always present in projection when the collection has a sidecar).
        if (meta.Translation is not null)
        {
            var translated = TemporalText.Kinds(meta.Fields.Where(f => f.Translatable), StringComparer.Ordinal);
            _declared.Add(config, Field("translations", "[Translation!]", ctx => TranslationList(ParentDict(ctx), translated)),
                FieldOrigin.Translations(meta));
        }

        return NewObject(config);
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
        var desc = registry.Get(meta.Name);
        if (rel.Kind == RelationKind.ManyToOne)
        {
            _declared.Add(config, Field(rel.Name, target, ctx => ParentDict(ctx).GetValueOrDefault(rel.Name)),
                FieldOrigin.Relation(meta, desc, rel));
            return;
        }

        var field = Field(rel.Name, $"[{target}!]", ctx => ParentDict(ctx).GetValueOrDefault(rel.Name));
        field.Arguments.Add(new ArgumentConfiguration("filter", null, TypeReference.Parse(SchemaTypeMapper.FilterInputName(rel.TargetCollection))));
        field.Arguments.Add(new ArgumentConfiguration("sort", null, TypeReference.Parse("[String!]")));
        field.Arguments.Add(new ArgumentConfiguration("limit", null, TypeReference.Parse("Int")));
        field.Arguments.Add(new ArgumentConfiguration("offset", null, TypeReference.Parse("Int")));
        _declared.Add(config, field, FieldOrigin.Relation(meta, desc, rel));

        // M2M relation carrying junction payload -> additive `<rel>Links: [<Rel>Link!]`
        // field reading the SAME underlying list as `<rel>` above (untouched); each element
        // is exposed as { node, junction } instead of the bare target.
        if (rel.Kind != RelationKind.ManyToMany) return;

        var descriptor = DescriptorFor(payloadRelations, rel.Name);
        if (descriptor is null) return;

        sink.Add(BuildJunctionType(meta, descriptor));
        sink.Add(BuildLinkType(meta, target, descriptor));
        _declared.Add(config, Field(
            SchemaTypeMapper.LinksFieldName(rel.Name),
            $"[{SchemaTypeMapper.LinkTypeName(meta.Name, rel.Name)}!]",
            ctx => ParentDict(ctx).GetValueOrDefault(rel.Name)), FieldOrigin.Links(meta, desc, rel));
    }

    private ObjectType BuildRepeaterItemType(CollectionMetadata meta, FieldMetadata repeater)
    {
        var desc = registry.Get(meta.Name);
        // RuntimeType = object (not IReadOnlyDictionary<string,object?>): the real parent at
        // execution time is a POCO (see ReadRepeaterSubField above), mirroring TagItemType.
        var config = new ObjectTypeConfiguration(
            SchemaTypeMapper.RepeaterItemTypeName(meta.Name, repeater.Name), null, typeof(object));
        foreach (var sub in repeater.Fields!)
        {
            if (sub.Hidden || SchemaTypeMapper.IsExcluded(sub.Interface)) continue;
            var sdl = SchemaTypeMapper.ScalarSdl(sub.Interface, typeof(string)); // lean scalar sub-field set
            if (sdl is null) continue;
            _declared.Add(config, Field(sub.Name, sdl, ctx => ReadRepeaterSubField(ctx.Parent<object>(), sub.Name)),
                FieldOrigin.SubField(meta, desc, repeater, sub));
        }
        return NewObject(config);
    }

    private InputObjectType BuildRepeaterItemInputType(CollectionMetadata meta, FieldMetadata repeater)
    {
        var desc = registry.Get(meta.Name);
        var config = new InputObjectTypeConfiguration(
            SchemaTypeMapper.RepeaterItemInputTypeName(meta.Name, repeater.Name), null,
            typeof(IReadOnlyDictionary<string, object?>));
        foreach (var sub in repeater.Fields!)
            if (WritableInput.SubField(sub).Sdl is { } sdl)
                _declared.Add(config, new InputFieldConfiguration(sub.Name, null, TypeReference.Parse(sdl)),
                    FieldOrigin.SubField(meta, desc, repeater, sub));
        return NewInput(config);
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
            _declared.Add(config, Field(pf.Name, sdl, ctx => ParentDict(ctx).GetValueOrDefault(pf.Name)), FieldOrigin.Payload(descriptor, pf));
        return NewObject(config);
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
    internal static M2MDescriptor? DescriptorFor(IReadOnlyList<M2MDescriptor> payloadRelations, string relationName) =>
        payloadRelations.FirstOrDefault(d => string.Equals(d.RelationName, relationName, StringComparison.OrdinalIgnoreCase));

    // Builds the per-parent-relation wrapper type used when an M2M relation carries junction
    // payload. It exposes a node field holding the target row and a junction field holding the
    // junction payload, one entry per element of the underlying relation list. The node field
    // reads the same element dict that the bare relation field's own sub-resolvers already read
    // through ParentDict, since it is already a full target row. The junction field reads the
    // internal junction key that deep expansion attaches, returning null when the caller lacks the
    // junction read grant, which is why that field's GraphQL type omits the required marker.
    private ObjectType BuildLinkType(CollectionMetadata meta, string targetTypeName, M2MDescriptor descriptor)
    {
        var config = new ObjectTypeConfiguration(
            SchemaTypeMapper.LinkTypeName(meta.Name, descriptor.RelationName), null,
            typeof(IReadOnlyDictionary<string, object?>));
        _declared.Add(config, Field("node", $"{targetTypeName}!", ctx => ParentDict(ctx)));
        _declared.Add(config, Field("junction", SchemaTypeMapper.JunctionTypeName(meta.Name, descriptor.RelationName),
            ctx => ParentDict(ctx).GetValueOrDefault("_junction")));
        return NewObject(config);
    }

    // <Parent><Rel>LinkInput: { id: ID!, <writable payload fields> }. JunctionPayload already
    // excludes ReadOnly/IsSystem columns (see RelationshipGraph.JunctionPayloadOf) — only Hidden
    // remains to filter here, mirroring AddWritableFields' writability rule.
    private InputObjectType BuildLinkInputType(CollectionMetadata meta, M2MDescriptor descriptor)
    {
        var config = new InputObjectTypeConfiguration(
            SchemaTypeMapper.LinkInputName(meta.Name, descriptor.RelationName), null,
            typeof(IReadOnlyDictionary<string, object?>));
        _declared.Add(config, new InputFieldConfiguration("id", null, TypeReference.Parse("ID!")));
        foreach (var pf in descriptor.JunctionPayload!)
        {
            if (pf.Hidden) continue;
            var iface = JunctionFieldInterface(descriptor, pf.Name, metadataProvider);
            if (iface is null) continue;
            var clr = descriptor.JunctionType.GetProperty(pf.Property)?.PropertyType;
            var sdl = SchemaTypeMapper.WritableInputSdl(iface.Value, clr);
            if (sdl is null) continue;
            _declared.Add(config, new InputFieldConfiguration(pf.Name, null, TypeReference.Parse(sdl)), FieldOrigin.Payload(descriptor, pf));
        }
        return NewInput(config);
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
        foreach (var f in meta.Fields.Where(f => f.Translatable && !f.IsSystem))
            if (WritableInput.TranslatedField(f, desc).Sdl is { } sdl)
                _declared.Add(config, new InputFieldConfiguration(f.Name, null, TypeReference.Parse(sdl)), FieldOrigin.Field(meta, desc, f));
        return NewInput(config);
    }

    // XTranslationInput: one entry = { locale: String!, fields: XTranslationFieldsInput }; an explicit null deletes the locale.
    private InputObjectType BuildTranslationInputType(CollectionMetadata meta)
    {
        var config = new InputObjectTypeConfiguration(
            SchemaTypeMapper.TranslationInputName(meta.Name), null,
            typeof(IReadOnlyDictionary<string, object?>));
        _declared.Add(config, new InputFieldConfiguration("locale", null, TypeReference.Parse("String!")));
        _declared.Add(config, new InputFieldConfiguration(
            "fields", null, TypeReference.Parse(SchemaTypeMapper.TranslationFieldsInputName(meta.Name))));
        return NewInput(config);
    }

    private ObjectType BuildListType(CollectionMetadata meta)
    {
        var config = new ObjectTypeConfiguration(SchemaTypeMapper.ListTypeName(meta.Name), null, typeof(PagedResultView));
        _declared.Add(config, Field("items", $"[{SchemaTypeMapper.TypeName(meta.Name)}!]!",
            ctx => ctx.Parent<PagedResultView>().Items));
        _declared.Add(config, Field("total", "Int!", ctx => ctx.Parent<PagedResultView>().Total));
        var own = TemporalText.Kinds(meta.Fields, StringComparer.OrdinalIgnoreCase);
        _declared.Add(config, Field("facets", "[FacetResult!]!",
            ctx => TemporalText.InFacets(ctx.Parent<PagedResultView>().Facets, meta, metadataProvider)));
        _declared.Add(config, Field("aggregate", "Any", ctx => TemporalText.InAggregate(ctx.Parent<PagedResultView>().Aggregate, own)));
        return NewObject(config);
    }

    private InputObjectType BuildFilterInput(CollectionMetadata meta, IReadOnlyList<M2MDescriptor> payloadRelations)
    {
        var name = SchemaTypeMapper.FilterInputName(meta.Name);
        var config = new InputObjectTypeConfiguration(name, null, typeof(IReadOnlyDictionary<string, object?>));
        _declared.Add(config, new InputFieldConfiguration("and", null, TypeReference.Parse($"[{name}!]")));
        _declared.Add(config, new InputFieldConfiguration("or", null, TypeReference.Parse($"[{name}!]")));
        _declared.Add(config, new InputFieldConfiguration("some", null, TypeReference.Parse(name)));
        _declared.Add(config, new InputFieldConfiguration("none", null, TypeReference.Parse(name)));
        AddFilterFields(config, meta, payloadRelations);
        return NewInput(config);
    }

    // Own-field + relation filter fields shared by BuildFilterInput (meta = the collection itself)
    // and BuildRelationFilterInput (meta = the RELATION'S TARGET collection — its own filterable
    // fields and relations, embedded under the parent's relation-specific filter input).
    private void AddFilterFields(
        InputObjectTypeConfiguration config, CollectionMetadata meta, IReadOnlyList<M2MDescriptor> payloadRelations)
    {
        var desc = registry.Get(meta.Name);
        _declared.Add(config, new InputFieldConfiguration("id", null, TypeReference.Parse("IdFilter")));

        var declared = new HashSet<string>(StringComparer.Ordinal);
        foreach (var f in meta.Fields)
        {
            if (f.Hidden || f.Ignored || SchemaTypeMapper.IsExcluded(f.Interface)) continue;
            if (!IsFilterable(f.Interface)) continue;
            var opInput = FilterInputTranslator.OperatorInputTypeName(f.Interface, ClrType(desc, f.Name));
            _declared.Add(config, new InputFieldConfiguration(f.Name, null, TypeReference.Parse(opInput)), FieldOrigin.Field(meta, desc, f));
            declared.Add(f.Name);
        }

        foreach (var rel in meta.Relations)
            AddRelationFilterField(config, meta, rel, payloadRelations, declared);
    }

    // Cross-relation filter inputs. M2O contributes its FK column as a filterable IdFilter (parity
    // with the REST allowlist) unless a filterable own field already declares that name or that field sets
    // Hidden = true (CollectionMetadata.OffersForeignKey), PLUS a nested
    // target FilterInput. O2M/M2M carry no FK on this collection, so they contribute only the nested
    // target FilterInput (ANY/EXISTS/predicate via
    // FilterInputTranslator). A ManyToMany relation carrying an exposable junction payload uses the
    // relation-specific <Parent><Rel>FilterInput instead of the plain <Target>FilterInput, so its
    // "junction" field is reachable from here. Referenced by name -> recursive / self-referential /
    // cyclic input types resolve like and/or (no build loop).
    private void AddRelationFilterField(
        InputObjectTypeConfiguration config, CollectionMetadata meta, RelationMetadata rel,
        IReadOnlyList<M2MDescriptor> payloadRelations, HashSet<string> declared)
    {
        string? targetFilter = null;
        var desc = registry.Get(meta.Name);
        if (rel.Kind == RelationKind.ManyToOne && rel.ForeignKey is { } fk)
        {
            if (meta.OffersForeignKey(rel) && !declared.Contains(fk))
                _declared.Add(config, new InputFieldConfiguration(fk, null, TypeReference.Parse("IdFilter")),
                    FieldOrigin.ForeignKey(meta, desc, rel));
            targetFilter = SchemaTypeMapper.FilterInputName(rel.TargetCollection);
        }
        else if (rel.Kind is RelationKind.OneToMany or RelationKind.ManyToMany)
        {
            targetFilter = rel.Kind == RelationKind.ManyToMany && DescriptorFor(payloadRelations, rel.Name) is not null
                ? SchemaTypeMapper.RelationFilterInputName(meta.Name, rel.Name)
                : SchemaTypeMapper.FilterInputName(rel.TargetCollection);
        }

        if (targetFilter is not null)
            _declared.Add(config, new InputFieldConfiguration(rel.Name, null, TypeReference.Parse(targetFilter)),
                FieldOrigin.Relation(meta, desc, rel));
    }

    // <Parent><Rel>FilterInput: the target collection's own filterable fields/relations (via
    // AddFilterFields against the TARGET's metadata) plus and/or/some/none of itself, plus a
    // `junction` field (only when the relation's payload has at least one filterable field —
    // `hasJunctionFilter`, computed by the caller from BuildJunctionFilterInput's own result so the
    // two can never disagree about whether the type/field pair exists).
    private InputObjectType BuildRelationFilterInput(CollectionMetadata meta, M2MDescriptor descriptor, bool hasJunctionFilter)
    {
        var name = SchemaTypeMapper.RelationFilterInputName(meta.Name, descriptor.RelationName);
        var config = new InputObjectTypeConfiguration(name, null, typeof(IReadOnlyDictionary<string, object?>));
        _declared.Add(config, new InputFieldConfiguration("and", null, TypeReference.Parse($"[{name}!]")));
        _declared.Add(config, new InputFieldConfiguration("or", null, TypeReference.Parse($"[{name}!]")));
        _declared.Add(config, new InputFieldConfiguration("some", null, TypeReference.Parse(name)));
        _declared.Add(config, new InputFieldConfiguration("none", null, TypeReference.Parse(name)));

        var targetMeta = metadataProvider.GetCollection(descriptor.TargetCollection)!;
        var targetPayloadRelations = m2mSource.M2MDescriptors(targetMeta.Name)
            .Where(d => HasExposablePayload(d, metadataProvider))
            .ToList();
        AddFilterFields(config, targetMeta, targetPayloadRelations);

        if (hasJunctionFilter)
            _declared.Add(config, new InputFieldConfiguration(
                "junction", null,
                TypeReference.Parse(SchemaTypeMapper.JunctionFilterInputName(meta.Name, descriptor.RelationName))));

        return NewInput(config);
    }

    // <Parent><Rel>JunctionFilterInput: one operator-input field per exposable junction payload
    // field that is also filterable (IsFilterable — same allowlist as an own-field) and has a
    // column (an Ignored field is selectable, never filterable). Returns null
    // when that set is empty, so the caller skips both this type AND the parent relation filter
    // input's "junction" field — a zero-field input type is rejected by HotChocolate at
    // schema-build time exactly like a zero-field object type (see HasExposablePayload's comment).
    private InputObjectType? BuildJunctionFilterInput(CollectionMetadata meta, M2MDescriptor descriptor)
    {
        var name = SchemaTypeMapper.JunctionFilterInputName(meta.Name, descriptor.RelationName);
        var config = new InputObjectTypeConfiguration(name, null, typeof(IReadOnlyDictionary<string, object?>));
        var junctionFields = descriptor.JunctionCollection is { } junctionCollection
            ? metadataProvider.GetCollection(junctionCollection)?.Fields
            : null;
        foreach (var (pf, iface, _) in ExposableJunctionFields(descriptor, metadataProvider))
        {
            if (!IsFilterable(iface)) continue;
            if (junctionFields?.Any(f => f.Ignored && string.Equals(f.Name, pf.Name, StringComparison.OrdinalIgnoreCase)) == true) continue;
            var clr = descriptor.JunctionType.GetProperty(pf.Property)?.PropertyType;
            var opInput = FilterInputTranslator.OperatorInputTypeName(iface, clr);
            _declared.Add(config, new InputFieldConfiguration(pf.Name, null, TypeReference.Parse(opInput)), FieldOrigin.Payload(descriptor, pf));
        }
        return config.Fields.Count == 0 ? null : NewInput(config);
    }

    private InputObjectType BuildCreateInput(CollectionMetadata meta, IReadOnlyList<M2MDescriptor> payloadRelations)
    {
        var config = new InputObjectTypeConfiguration(
            SchemaTypeMapper.CreateInputName(meta.Name), null, typeof(IReadOnlyDictionary<string, object?>));
        AddWritableFields(config, meta, payloadRelations);
        return NewInput(config);
    }

    private InputObjectType BuildUpdateInput(CollectionMetadata meta, IReadOnlyList<M2MDescriptor> payloadRelations)
    {
        var config = new InputObjectTypeConfiguration(
            SchemaTypeMapper.UpdateInputName(meta.Name), null, typeof(IReadOnlyDictionary<string, object?>));
        // Optimistic-concurrency token (update-only). Absent -> no protection (backward compatible).
        _declared.Add(config, new InputFieldConfiguration("version", null, TypeReference.Parse("Long")));
        AddWritableFields(config, meta, payloadRelations);
        return NewInput(config);
    }

    // Shared by create/update inputs: writable own-fields (scalars, File/Image, Files, multi-value,
    // Json/KeyValue), Tags, Repeater, and M2M foreign-key arrays — all nullable. Translatable own-fields
    // are excluded — translations use the dedicated typed translations input instead.
    private void AddWritableFields(
        InputObjectTypeConfiguration config, CollectionMetadata meta, IReadOnlyList<M2MDescriptor> payloadRelations)
    {
        foreach (var field in WritableInput.Fields(meta, registry.Get(meta.Name), payloadRelations))
            _declared.Add(config, new InputFieldConfiguration(field.Name, null, TypeReference.Parse(field.Sdl)), field.Origin);
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
    internal static Type? ClrType(EntityDescriptor? desc, string fieldName)
    {
        if (desc is null) return null;
        return desc.FieldToProperty.TryGetValue(fieldName, out var prop)
            ? desc.EntityType.GetProperty(prop)?.PropertyType
            : null;
    }

    private static List<object> TranslationList(IReadOnlyDictionary<string, object?> parent, IReadOnlyDictionary<string, FieldInterface> kinds)
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
                ["fields"] = TemporalText.InFields(entry.Value, kinds)
            });
        }
        return result;
    }

    // A String field that holds a DateTime is a Time field: its text is the UTC instant (see TemporalText).
    internal static ObjectFieldConfiguration Field(string name, string sdl, PureFieldDelegate pure)
        => new(name, null, TypeReference.Parse(sdl), pureResolver: sdl == "String" ? ctx => TemporalText.Instant(pure(ctx)) : pure);
}

/// <summary>Adapter so the ArticleList type reads items/total/facets/aggregate from the
/// Application PagedResult and MetaWire.Aggregate's wire shape.</summary>
internal sealed record PagedResultView(
    IReadOnlyList<object> Items, int Total,
    IReadOnlyList<FacetResult> Facets, IReadOnlyDictionary<string, IReadOnlyDictionary<string, object?>>? Aggregate);
