using System.Reflection;
using Struo.Application.Metadata;
using Struo.Domain.Metadata;
using Struo.Domain.Metadata.Enums;
using Struo.Domain.Metadata.Models;

namespace Struo.Infrastructure.Metadata;

/// <summary>
/// Infrastructure-only richer descriptor that carries SqlSugar-specific data
/// (junction Type, FK property names) for the expander and junction-sync.
/// Application-layer code only sees <see cref="IRelationshipGraph"/> which
/// exposes the serialisable <see cref="RelationMetadata"/> surface.
/// </summary>
public sealed record RelationDescriptor(
    RelationMetadata Meta,
    Type? JunctionType,
    string? JunctionParentFk,
    string? JunctionTargetFk,
    string? JunctionSort,
    string? ReverseForeignKeyProperty);

/// <summary>
/// Singleton graph of all collection relations, built once at startup.
/// Registered as both <see cref="IRelationshipGraph"/> and the concrete
/// <see cref="RelationshipGraph"/> so Infrastructure peers can access descriptors.
/// </summary>
public sealed class RelationshipGraph : IRelationshipGraph
{
    private readonly IReadOnlyDictionary<string, IReadOnlyList<RelationDescriptor>> _byCollection;
    private readonly IReadOnlyDictionary<string, IReadOnlyList<(string, string)>> _inboundRestrict;

    public RelationshipGraph(
        IReadOnlyList<CollectionMetadata> collections,
        IReadOnlyDictionary<string, Type> collectionTypes)
    {
        var known = collections.Select(c => c.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var map = new Dictionary<string, IReadOnlyList<RelationDescriptor>>(StringComparer.OrdinalIgnoreCase);
        var inbound = new Dictionary<string, List<(string, string)>>(StringComparer.OrdinalIgnoreCase);

        foreach (var c in collections)
        {
            var entityType = collectionTypes[c.Name];
            var descriptors = new List<RelationDescriptor>();

            foreach (var r in c.Relations)
            {
                // Consistency validation: target must be a known CmsCollection
                if (!known.Contains(r.TargetCollection))
                    throw new MetadataException(
                        $"Relation '{c.Name}.{r.Name}' targets unknown collection '{r.TargetCollection}'.");

                // Consistency validation: M2O must have a non-empty foreign key
                if (r.Kind == RelationKind.ManyToOne && string.IsNullOrEmpty(r.ForeignKey))
                    throw new MetadataException(
                        $"M2O relation '{c.Name}.{r.Name}' has no foreign key.");

                descriptors.Add(BuildDescriptor(entityType, r));

                // Build inbound-restrict index for delete-guard
                if (r.Kind == RelationKind.ManyToOne && r.OnDelete == OnDelete.Restrict)
                {
                    if (!inbound.TryGetValue(r.TargetCollection, out var lst))
                        inbound[r.TargetCollection] = lst = [];
                    lst.Add((c.Name, r.ForeignKey!));
                }
            }

            map[c.Name] = descriptors;
        }

        _byCollection = map;
        _inboundRestrict = inbound.ToDictionary(
            k => k.Key,
            v => (IReadOnlyList<(string, string)>)v.Value,
            StringComparer.OrdinalIgnoreCase);
    }

    // ── IRelationshipGraph ───────────────────────────────────────────────────

    public IReadOnlyList<RelationMetadata> Relations(string collection) =>
        _byCollection.TryGetValue(collection, out var d)
            ? d.Select(x => x.Meta).ToList()
            : [];

    public RelationMetadata? Resolve(string collection, string relationName) =>
        Relations(collection).FirstOrDefault(r =>
            string.Equals(r.Name, relationName, StringComparison.OrdinalIgnoreCase));

    public IReadOnlyList<(string SourceCollection, string ForeignKey)> InboundRestrict(
        string targetCollection) =>
        _inboundRestrict.TryGetValue(targetCollection, out var l) ? l : [];

    // ── Infrastructure-only descriptor access ────────────────────────────────

    public IReadOnlyList<RelationDescriptor> Descriptors(string collection) =>
        _byCollection.TryGetValue(collection, out var d) ? d : [];

    // ── Helpers ─────────────────────────────────────────────────────────────

    private static RelationDescriptor BuildDescriptor(Type entityType, RelationMetadata r)
    {
        // Find the nav property by matching camelCase name
        var prop = entityType.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .First(p => string.Equals(
                System.Text.Json.JsonNamingPolicy.CamelCase.ConvertName(p.Name),
                r.Name,
                StringComparison.OrdinalIgnoreCase));

        var navData = prop.CustomAttributes
            .First(a => a.AttributeType.FullName == "SqlSugar.Navigate");

        if (r.Kind == RelationKind.ManyToMany)
        {
            return new RelationDescriptor(
                r,
                JunctionType: MetadataScanner.NavigateMappingType(navData),
                JunctionParentFk: MetadataScanner.NavigateMappingA(navData),
                JunctionTargetFk: MetadataScanner.NavigateMappingB(navData),
                JunctionSort: prop.GetCustomAttribute<Struo.Domain.Metadata.Attributes.CmsRelationAttribute>()?.SortField,
                ReverseForeignKeyProperty: null);
        }

        if (r.Kind == RelationKind.OneToMany)
        {
            // ReverseForeignKeyProperty = the FK property name on the child entity
            return new RelationDescriptor(
                r,
                JunctionType: null,
                JunctionParentFk: null,
                JunctionTargetFk: null,
                JunctionSort: null,
                ReverseForeignKeyProperty: MetadataScanner.NavigateForeignKeyName(navData));
        }

        // ManyToOne — FK is already captured in Meta.ForeignKey
        return new RelationDescriptor(r, null, null, null, null, null);
    }
}
