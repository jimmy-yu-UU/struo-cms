// src/Struo.Api/GraphQl/WritableInput.cs
using Struo.Application.Metadata;
using Struo.Domain.Metadata;
using Struo.Domain.Metadata.Enums;
using Struo.Domain.Metadata.Models;

namespace Struo.Api.GraphQl;

/// <summary>
/// The fields of a collection's input types. Each input type has one rule for which field goes in:
/// <see cref="OwnField"/> (create and update), <see cref="TranslatedField"/> (the translations input) and
/// <see cref="SubField"/> (a Repeater item). The schema builder adds the fields these rules accept, and
/// <see cref="RequireAny"/> reads the same rules, so what the startup check counts is what the schema
/// contains: the GraphQL library cannot build an input type with no field.
/// </summary>
internal static class WritableInput
{
    internal sealed record InputField(string Name, string Sdl);

    /// <summary>The input type of a field, or why the field is not in the input type.</summary>
    internal readonly record struct Decision(string? Sdl, string? Skip);

    private const int ListedFields = 5;

    private static Decision Skipped(string reason) => new(null, reason);

    private static string? FlagReason(FieldMetadata f)
    {
        if (f.IsSystem) return "an audit field";
        if (f.Hidden) return "Hidden = true";
        return f.ReadOnly ? "ReadOnly = true" : null;
    }

    private static Decision NeverWritten(FieldMetadata f, string? sdl) =>
        sdl is null ? Skipped($"Interface = {f.Interface}, which is never written") : new Decision(sdl, null);

    /// <summary>A field of the create and update input types.</summary>
    internal static Decision OwnField(CollectionMetadata meta, FieldMetadata f, EntityDescriptor? desc)
    {
        if (FlagReason(f) is { } flag) return Skipped(flag);
        if (f.Translatable) return Skipped("translatable (it goes through the translations input)");

        return f.Interface switch
        {
            FieldInterface.Tags => new Decision($"[{SchemaTypeMapper.TagItemInputName}!]", null),
            FieldInterface.Repeater when f.Fields is not { Count: > 0 } => Skipped("a Repeater without sub-fields"),
            FieldInterface.Repeater when !f.Fields.Any(s => SubField(s).Sdl is not null) =>
                Skipped("a Repeater with no writable sub-field"),
            FieldInterface.Repeater =>
                new Decision($"[{SchemaTypeMapper.RepeaterItemInputTypeName(meta.Name, f.Name)}!]", null),
            _ => NeverWritten(f, SchemaTypeMapper.WritableInputSdl(f.Interface, CollectionSchemaBuilder.ClrType(desc, f.Name))),
        };
    }

    /// <summary>A translatable field of the translations input type.</summary>
    internal static Decision TranslatedField(FieldMetadata f, EntityDescriptor? desc) =>
        FlagReason(f) is { } flag
            ? Skipped(flag)
            : NeverWritten(f, SchemaTypeMapper.WritableInputSdl(f.Interface, CollectionSchemaBuilder.ClrType(desc, f.Name)));

    /// <summary>A sub-field of a Repeater item input type.</summary>
    internal static Decision SubField(FieldMetadata sub) =>
        FlagReason(sub) is { } flag
            ? Skipped(flag)
            : NeverWritten(sub, SchemaTypeMapper.WritableInputSdl(sub.Interface, typeof(string)));

    private static List<FieldMetadata> Translatable(CollectionMetadata meta) =>
        meta.Fields.Where(f => f.Translatable && !f.IsSystem).ToList();

    private static bool IsServerOwned(CollectionMetadata meta, string name) => meta.ServerOwnedKeyNames().Contains(name);

    internal static IEnumerable<InputField> Fields(
        CollectionMetadata meta, EntityDescriptor? desc, IReadOnlyList<M2MDescriptor> payloadRelations)
    {
        var own = OwnFields(meta, desc);
        foreach (var field in own)
            yield return field;

        var declared = own.Select(f => f.Name).ToHashSet(StringComparer.Ordinal);
        foreach (var field in meta.Relations.SelectMany(rel => RelationFields(meta, rel, payloadRelations, declared)))
            yield return field;

        if (meta.Translation is not null && Translatable(meta).Any(f => TranslatedField(f, desc).Sdl is not null))
            yield return new InputField("translations", $"[{SchemaTypeMapper.TranslationInputName(meta.Name)}!]");
    }

    private static List<InputField> OwnFields(CollectionMetadata meta, EntityDescriptor? desc)
    {
        var own = new List<InputField>();
        foreach (var f in meta.Fields)
            if (OwnField(meta, f, desc) is { Sdl: { } sdl })
                own.Add(new InputField(f.Name, sdl));
        return own;
    }

    // A foreign key that is also a writable [CmsField] is already in the input as that field, in the form the
    // field declares; a ReadOnly or audit key is not written, as for the field itself, and a key whose field sets
    // Hidden = true is not offered by name.
    private static IEnumerable<InputField> RelationFields(
        CollectionMetadata meta, RelationMetadata rel, IReadOnlyList<M2MDescriptor> payloadRelations,
        HashSet<string> declared)
    {
        if (rel.Kind == RelationKind.ManyToOne && rel.ForeignKey is { } fk)
        {
            if (meta.OffersForeignKey(rel) && !declared.Contains(fk) && !IsServerOwned(meta, fk))
                yield return new InputField(fk, "ID");
        }
        else if (rel.Kind == RelationKind.ManyToMany)
        {
            yield return new InputField(rel.Name, "[ID!]");
            if (CollectionSchemaBuilder.DescriptorFor(payloadRelations, rel.Name) is not null)
                yield return new InputField(
                    SchemaTypeMapper.LinksFieldName(rel.Name),
                    $"[{SchemaTypeMapper.LinkInputName(meta.Name, rel.Name)}!]");
        }
    }

    /// <summary>
    /// Stops the host with a message about the collection when one of its input types would have no field,
    /// which the GraphQL library cannot build. The most specific cause is reported first: the translations
    /// input, then a Repeater item input, then the create input.
    /// </summary>
    internal static void RequireAny(
        CollectionMetadata meta, EntityDescriptor? desc, IReadOnlyList<M2MDescriptor> payloadRelations)
    {
        RequireTranslatedField(meta, desc);
        foreach (var repeater in meta.Fields.Where(f => f is { Interface: FieldInterface.Repeater, Fields.Count: > 0 }))
            RequireSubField(meta, repeater);
        if (Fields(meta, desc, payloadRelations).Any()) return;

        var skipped = meta.Fields.Where(f => !f.IsSystem).Select(f => $"'{f.Name}' ({OwnField(meta, f, desc).Skip})").ToList();
        var listed = skipped.Count == 0
            ? "The collection declares no [CmsField] property."
            : $"None of its fields counts: {Bounded(skipped)}.";
        throw new MetadataException(
            $"Collection '{meta.Name}' has nothing GraphQL can write, so its create input type would be empty and the " +
            $"schema cannot be built. {listed} " +
            "Add a [CmsField] property that GraphQL can write: it must not set Hidden = true or ReadOnly = true, and its " +
            "interface must not be Hidden, Divider or Password. A many-to-many relation, or a many-to-one relation " +
            "whose foreign key is not Hidden = true, not ReadOnly = true and not an audit field, also counts.");
    }

    private static void RequireTranslatedField(CollectionMetadata meta, EntityDescriptor? desc)
    {
        if (meta.Translation is null) return;
        var fields = Translatable(meta);
        if (fields.Any(f => TranslatedField(f, desc).Sdl is not null)) return;

        var skipped = fields.Select(f => $"'{f.Name}' ({TranslatedField(f, desc).Skip})").ToList();
        throw new MetadataException(
            $"Collection '{meta.Name}' has a translation sidecar, but none of its translatable fields can be written " +
            "through GraphQL, so its translations input type would be empty and the schema cannot be built. " +
            $"None of them counts: {Bounded(skipped)}. " +
            "Make at least one translatable field writable: it must not set Hidden = true, and its " +
            "interface must not be Hidden, Divider or Password.");
    }

    private static void RequireSubField(CollectionMetadata meta, FieldMetadata repeater)
    {
        if (repeater.Fields!.Any(s => SubField(s).Sdl is not null)) return;

        var skipped = repeater.Fields!.Select(s => $"'{s.Name}' ({SubField(s).Skip})").ToList();
        throw new MetadataException(
            $"Repeater '{meta.Name}.{repeater.Name}' has no sub-field GraphQL can write, so its item input type would be " +
            $"empty and the schema cannot be built. None of its sub-fields counts: {Bounded(skipped)}. " +
            "Make at least one sub-field writable: it must not set Hidden = true or ReadOnly = true.");
    }

    private static string Bounded(List<string> items) =>
        items.Count <= ListedFields
            ? string.Join(", ", items)
            : $"{string.Join(", ", items.Take(ListedFields))} and {items.Count - ListedFields} more";
}
