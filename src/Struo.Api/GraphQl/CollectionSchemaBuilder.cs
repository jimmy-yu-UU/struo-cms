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
internal sealed class CollectionSchemaBuilder(IEntityRegistry registry)
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
        var relationNames = meta.Relations.Select(r => r.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);

        types.Add(BuildObjectType(meta, relationNames, types));   // Article + nested repeater item types (added to `types`)
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

        types.Add(BuildCreateInput(meta));                         // ArticleCreateInput
        types.Add(BuildUpdateInput(meta));                         // ArticleUpdateInput
        return types;
    }

    private ObjectType BuildObjectType(CollectionMetadata meta, HashSet<string> relationNames, List<ITypeSystemMember> sink)
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
        {
            var target = SchemaTypeMapper.TypeName(rel.TargetCollection);
            if (rel.Kind == RelationKind.ManyToOne)
            {
                config.Fields.Add(Field(rel.Name, target, ctx => ParentDict(ctx).GetValueOrDefault(rel.Name)));
            }
            else
            {
                var field = Field(rel.Name, $"[{target}!]", ctx => ParentDict(ctx).GetValueOrDefault(rel.Name));
                field.Arguments.Add(new ArgumentConfiguration("filter", null, TypeReference.Parse($"{target}FilterInput")));
                field.Arguments.Add(new ArgumentConfiguration("sort", null, TypeReference.Parse("[String!]")));
                field.Arguments.Add(new ArgumentConfiguration("limit", null, TypeReference.Parse("Int")));
                field.Arguments.Add(new ArgumentConfiguration("offset", null, TypeReference.Parse("Int")));
                config.Fields.Add(field);
            }
        }

        // translations map (always present in projection when the collection has a sidecar).
        if (meta.Translation is not null)
            config.Fields.Add(Field("translations", "[Translation!]", ctx => TranslationList(ParentDict(ctx))));

        return ObjectType.CreateUnsafe(config);
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

    private InputObjectType BuildCreateInput(CollectionMetadata meta)
    {
        var config = new InputObjectTypeConfiguration(
            SchemaTypeMapper.CreateInputName(meta.Name), null, typeof(IReadOnlyDictionary<string, object?>));
        AddWritableFields(config, meta);
        return InputObjectType.CreateUnsafe(config);
    }

    private InputObjectType BuildUpdateInput(CollectionMetadata meta)
    {
        var config = new InputObjectTypeConfiguration(
            SchemaTypeMapper.UpdateInputName(meta.Name), null, typeof(IReadOnlyDictionary<string, object?>));
        // Optimistic-concurrency token (update-only). Absent -> no protection (backward compatible).
        config.Fields.Add(new InputFieldConfiguration("version", null, TypeReference.Parse("Long")));
        AddWritableFields(config, meta);
        return InputObjectType.CreateUnsafe(config);
    }

    // Shared by create/update inputs: writable own-fields (scalars, File/Image, Files, multi-value,
    // Json/KeyValue), Tags, Repeater, and M2M foreign-key arrays — all nullable. Translatable own-fields
    // are excluded — translations use the dedicated typed translations input instead.
    private void AddWritableFields(InputObjectTypeConfiguration config, CollectionMetadata meta)
    {
        var desc = registry.Get(meta.Name);
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

        // M2O foreign keys (as ID) + M2M relations (as [ID!] target-id arrays).
        foreach (var rel in meta.Relations)
        {
            if (rel.Kind == RelationKind.ManyToOne && rel.ForeignKey is { } fk)
                config.Fields.Add(new InputFieldConfiguration(fk, null, TypeReference.Parse("ID")));
            else if (rel.Kind == RelationKind.ManyToMany)
                config.Fields.Add(new InputFieldConfiguration(rel.Name, null, TypeReference.Parse("[ID!]")));
        }

        // Typed translations input — only when the collection has a translation sidecar.
        // Translatable own-fields stay excluded above (`if (f.Translatable) continue;`); they are
        // carried here instead.
        if (meta.Translation is not null)
            config.Fields.Add(new InputFieldConfiguration(
                "translations", null,
                TypeReference.Parse($"[{SchemaTypeMapper.TranslationInputName(meta.Name)}!]")));
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
