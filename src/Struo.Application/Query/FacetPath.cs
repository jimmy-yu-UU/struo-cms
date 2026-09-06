// src/Struo.Application/Query/FacetPath.cs
using Struo.Application.Metadata;
using Struo.Domain.Metadata.Enums;
using Struo.Domain.Metadata.Models;
using Struo.Domain.Query;

namespace Struo.Application.Query;

public enum FacetPathKind { OwnField, ForeignKey, Relation, RelationLeaf }

/// <summary>
/// A validated facet path: an own scalar field, a many-to-one foreign key column, a relation name
/// itself (bucketing by the related row), or exactly one relation hop plus a scalar leaf on the
/// target collection. Facet paths support at most one relation hop — see
/// <see cref="FacetPathResolver.Resolve"/>.
/// </summary>
public sealed record ResolvedFacetPath(string Raw, FacetPathKind Kind, string? OwnField, RelationMetadata? Relation, string? LeafField)
{
    public string? TargetCollection => Relation?.TargetCollection;
}

/// <summary>
/// Resolves a raw dotted facet path (from <c>QueryModel.Facets</c>) against a root collection's
/// metadata and the relationship graph. Deliberately independent of <see cref="RelationPath"/>: a
/// facet path never carries a quantifier or a <c>_junction</c> segment, and is capped at one relation
/// hop, so it does not need that type's deeper-path machinery.
/// </summary>
public static class FacetPathResolver
{
    /// <summary>Field interfaces a facet bucket count can meaningfully group by. Excludes long-form
    /// text (RichText/Markdown/Code), multi-value interfaces (MultiSelect/CheckboxGroup/Tags/Repeater),
    /// structured payloads (Json/KeyValue/Files), and Hidden/Divider/Password.</summary>
    public static readonly IReadOnlySet<FieldInterface> Facetable = new HashSet<FieldInterface>
    {
        FieldInterface.Text, FieldInterface.Textarea, FieldInterface.Slug, FieldInterface.Email, FieldInterface.Url,
        FieldInterface.Color, FieldInterface.Phone, FieldInterface.Select, FieldInterface.Radio, FieldInterface.Number,
        FieldInterface.Slider, FieldInterface.Rating, FieldInterface.Boolean, FieldInterface.Checkbox, FieldInterface.Date,
        FieldInterface.DateTime, FieldInterface.Time, FieldInterface.Uuid, FieldInterface.File, FieldInterface.Image,
    };

    public static ResolvedFacetPath Resolve(CollectionMetadata root, string raw, IRelationshipGraph graph, IMetadataProvider metadata)
    {
        if (string.IsNullOrWhiteSpace(raw)) throw new QueryException("Facet path must not be empty.");
        var parts = raw.Split('.');
        if (parts.Any(p => p.StartsWith('_')))
            throw new QueryException($"Facet paths cannot contain quantifiers or '_junction': '{raw}'.");
        if (parts.Length > 2) throw new QueryException($"Facet paths support exactly one relation hop: '{raw}'.");
        return parts.Length == 1 ? ResolveSingle(root, raw, graph) : ResolveLeaf(root, raw, parts[0], parts[1], graph, metadata);
    }

    private static ResolvedFacetPath ResolveSingle(CollectionMetadata root, string raw, IRelationshipGraph graph)
    {
        var field = VisibleField(root, raw);
        if (field is not null)
        {
            RequireFacetable(root, field);
            return new ResolvedFacetPath(raw, FacetPathKind.OwnField, field.Name, null, null);
        }
        var fk = root.Relations.FirstOrDefault(r => r.Kind == RelationKind.ManyToOne
            && string.Equals(r.ForeignKey, raw, StringComparison.OrdinalIgnoreCase));
        if (fk is not null) return new ResolvedFacetPath(raw, FacetPathKind.ForeignKey, fk.ForeignKey, fk, null);
        var rel = graph.Resolve(root.Name, raw);
        if (rel is not null) return new ResolvedFacetPath(raw, FacetPathKind.Relation, null, rel, null);
        throw new QueryException($"Unknown field '{raw}' on collection '{root.Name}'.");
    }

    private static ResolvedFacetPath ResolveLeaf(CollectionMetadata root, string raw, string relationName, string leaf,
        IRelationshipGraph graph, IMetadataProvider metadata)
    {
        var rel = graph.Resolve(root.Name, relationName)
            ?? throw new QueryException($"Unknown relation '{relationName}' on collection '{root.Name}'.");
        var target = metadata.GetCollection(rel.TargetCollection)
            ?? throw new QueryException($"Unknown collection '{rel.TargetCollection}'.");
        var field = VisibleField(target, leaf)
            ?? throw new QueryException($"Unknown field '{leaf}' on collection '{target.Name}'.");
        RequireFacetable(target, field);
        return new ResolvedFacetPath(raw, FacetPathKind.RelationLeaf, null, rel, field.Name);
    }

    // Excludes hidden fields so a facet path can never reveal a hidden field's existence: an
    // unresolvable name and a hidden one both fall through to the same "Unknown field" message.
    private static FieldMetadata? VisibleField(CollectionMetadata meta, string name) =>
        meta.Fields.FirstOrDefault(f => !f.Hidden && string.Equals(f.Name, name, StringComparison.OrdinalIgnoreCase));

    private static void RequireFacetable(CollectionMetadata meta, FieldMetadata field)
    {
        if (!Facetable.Contains(field.Interface))
            throw new QueryException($"Field '{field.Name}' on collection '{meta.Name}' ({field.Interface}) cannot be used as a facet.");
    }
}
