// src/Struo.Api/GraphQl/WritableInput.cs
using Struo.Application.Metadata;
using Struo.Domain.Metadata;
using Struo.Domain.Metadata.Enums;
using Struo.Domain.Metadata.Models;

namespace Struo.Api.GraphQl;

/// <summary>
/// The fields of a collection's create and update input types. The schema builder adds exactly the fields
/// <see cref="Fields"/> returns, and <see cref="RequireAny"/> reads the same list, so what the startup
/// check counts is what the schema contains.
/// </summary>
internal static class WritableInput
{
    internal sealed record InputField(string Name, string Sdl);

    /// <summary>Why an own field is not an input field, or null when it is one.</summary>
    internal static string? SkipReason(FieldMetadata f)
    {
        if (f.IsSystem) return "an audit field";
        if (f.Hidden) return "Hidden = true";
        if (f.ReadOnly) return "ReadOnly = true";
        if (f.Translatable) return "translatable (it goes through the translations input)";
        if (SchemaTypeMapper.IsExcluded(f.Interface)) return $"Interface = {f.Interface}, which is never written";
        if (f.Interface == FieldInterface.Repeater && f.Fields is not { Count: > 0 }) return "a Repeater without sub-fields";
        return null;
    }

    internal static string OwnFieldSdl(CollectionMetadata meta, FieldMetadata f, EntityDescriptor? desc) =>
        f.Interface switch
        {
            FieldInterface.Tags => $"[{SchemaTypeMapper.TagItemInputName}!]",
            FieldInterface.Repeater => $"[{SchemaTypeMapper.RepeaterItemInputTypeName(meta.Name, f.Name)}!]",
            _ => SchemaTypeMapper.WritableInputSdl(f.Interface, CollectionSchemaBuilder.ClrType(desc, f.Name))
                 ?? throw new InvalidOperationException(
                     $"Field '{meta.Name}.{f.Name}' passed the input-field check but has no input type."),
        };

    internal static IEnumerable<InputField> Fields(
        CollectionMetadata meta, EntityDescriptor? desc, IReadOnlyList<M2MDescriptor> payloadRelations)
    {
        foreach (var f in meta.Fields.Where(f => SkipReason(f) is null))
            yield return new InputField(f.Name, OwnFieldSdl(meta, f, desc));

        foreach (var rel in meta.Relations)
        {
            if (rel.Kind == RelationKind.ManyToOne && rel.ForeignKey is { } fk)
                yield return new InputField(fk, "ID");
            else if (rel.Kind == RelationKind.ManyToMany)
            {
                yield return new InputField(rel.Name, "[ID!]");
                if (CollectionSchemaBuilder.DescriptorFor(payloadRelations, rel.Name) is not null)
                    yield return new InputField(
                        SchemaTypeMapper.LinksFieldName(rel.Name),
                        $"[{SchemaTypeMapper.LinkInputName(meta.Name, rel.Name)}!]");
            }
        }

        if (meta.Translation is not null)
            yield return new InputField("translations", $"[{SchemaTypeMapper.TranslationInputName(meta.Name)}!]");
    }

    /// <summary>
    /// Stops the host with a message about the collection when its create input type would have no field,
    /// which the GraphQL library cannot build.
    /// </summary>
    internal static void RequireAny(
        CollectionMetadata meta, EntityDescriptor? desc, IReadOnlyList<M2MDescriptor> payloadRelations)
    {
        if (Fields(meta, desc, payloadRelations).Any()) return;

        var skipped = meta.Fields
            .Where(f => !f.IsSystem)
            .Select(f => $"'{f.Name}' ({SkipReason(f)})")
            .ToList();
        var listed = skipped.Count == 0 ? "The collection declares no [CmsField] property." : $"None of its fields counts: {string.Join(", ", skipped)}.";
        throw new MetadataException(
            $"Collection '{meta.Name}' has nothing GraphQL can write, so its create input type would be empty and the " +
            $"schema cannot be built. {listed} " +
            "Add a [CmsField] property that is not Hidden, ReadOnly, Password or Divider and does not use Hidden = true, " +
            "or a many-to-one or many-to-many relation.");
    }
}
