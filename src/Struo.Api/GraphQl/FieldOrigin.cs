// src/Struo.Api/GraphQl/FieldOrigin.cs
using System.Reflection;
using System.Text.Json;
using Struo.Domain.Metadata.Attributes;
using Struo.Application.Metadata;
using Struo.Domain.Metadata.Enums;
using Struo.Domain.Metadata.Models;

namespace Struo.Api.GraphQl;

/// <summary>
/// The text that names a GraphQL field's declaration in the developer's terms: the class and property for a
/// field or a relation, the collection for a junction payload field. <see cref="DeclaredFields"/> prints it.
/// </summary>
internal static class FieldOrigin
{
    internal static string Field(CollectionMetadata meta, EntityDescriptor? desc, FieldMetadata field) =>
        $"the {field.Interface} field '{Member(meta, desc, field.Name, Occurrence(meta, field))}'";

    internal static string FileObject(CollectionMetadata meta, EntityDescriptor? desc, FieldMetadata field, string exposedAs)
    {
        var kind = field.Interface == FieldInterface.Files ? "list" : "object";
        return $"the {field.Interface} field '{Member(meta, desc, field.Name, Occurrence(meta, field))}', whose File {kind} is exposed as '{exposedAs}'";
    }

    internal static string Relation(CollectionMetadata meta, EntityDescriptor? desc, RelationMetadata relation) =>
        $"the relation '{Member(meta, desc, relation.Name)}'";

    internal static string ForeignKey(CollectionMetadata meta, EntityDescriptor? desc, RelationMetadata relation) =>
        $"the foreign key '{Member(meta, desc, relation.ForeignKey ?? relation.Name)}' of {Relation(meta, desc, relation)}";

    internal static string Links(CollectionMetadata meta, EntityDescriptor? desc, RelationMetadata relation) =>
        $"the links of the many-to-many relation '{Member(meta, desc, relation.Name)}' with junction payload";

    internal static string SubField(CollectionMetadata meta, EntityDescriptor? desc, FieldMetadata repeater, FieldMetadata sub) =>
        $"the {sub.Interface} sub-field '{sub.Name}' of the Repeater '{Member(meta, desc, repeater.Name)}'";

    internal static string Translations(CollectionMetadata meta) =>
        $"the translations of the translation sidecar of '{meta.Name}'";

    internal static string Payload(M2MDescriptor descriptor, JunctionPayloadField payload) =>
        $"the payload field '{descriptor.JunctionType.Name}.{PropertyOf(descriptor.JunctionType, payload.Name, 0)?.Name ?? payload.Property}' of the junction of '{descriptor.RelationName}'";

    // Which of the fields that share a name this one is, in declaration order.
    private static int Occurrence(CollectionMetadata meta, FieldMetadata field)
    {
        var occurrence = 0;
        foreach (var other in meta.Fields)
        {
            if (ReferenceEquals(other, field)) return occurrence;
            if (string.Equals(other.Name, field.Name, StringComparison.Ordinal)) occurrence++;
        }
        return 0;
    }

    // The properties of a type that GraphQL exposes under a name: those a field or a relation declares, else any.
    private static PropertyInfo? PropertyOf(Type type, string name, int occurrence)
    {
        var named = type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => string.Equals(JsonNamingPolicy.CamelCase.ConvertName(p.Name), name, StringComparison.Ordinal))
            .ToList();
        var declared = named
            .Where(p => p.IsDefined(typeof(CmsFieldAttribute), true) || p.IsDefined(typeof(CmsRelationAttribute), true))
            .ToList();
        var candidates = declared.Count > 0 ? declared : named;
        return candidates.Count == 0 ? null : candidates[Math.Min(occurrence, candidates.Count - 1)];
    }

    // Class.Property for a name the collection exposes: the entity's property, else the translation class's, else the bare name.
    private static string Member(CollectionMetadata meta, EntityDescriptor? desc, string name, int occurrence = 0)
    {
        foreach (var type in new[] { desc?.EntityType, meta.Translation?.TranslationEntityType })
            if (type is not null && PropertyOf(type, name, occurrence) is { } property)
                return $"{property.DeclaringType!.Name}.{property.Name}";
        return $"{meta.Name}.{name}";
    }
}
