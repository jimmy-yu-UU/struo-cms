using System.Reflection;
using SqlSugar;
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
    string? ReverseForeignKeyProperty,
    string? JunctionCollection = null,
    IReadOnlyList<JunctionPayloadField>? JunctionPayload = null);

/// <summary>
/// Singleton graph of all collection relations, built once at startup.
/// Registered as both <see cref="IRelationshipGraph"/> and the concrete
/// <see cref="RelationshipGraph"/> so Infrastructure peers can access descriptors.
/// </summary>
public sealed class RelationshipGraph : IRelationshipGraph, IM2MDescriptorSource
{
    private readonly IReadOnlyList<CollectionMetadata> _collections;
    private readonly IReadOnlyDictionary<string, IReadOnlyList<RelationDescriptor>> _byCollection;
    private readonly IReadOnlyDictionary<string, IReadOnlyList<(string, string)>> _inboundRestrict;
    private readonly IReadOnlyDictionary<string, IReadOnlyList<(string, string)>> _inboundSetNull;
    private readonly IReadOnlyDictionary<string, IReadOnlyList<(string, string)>> _inboundCascade;

    public RelationshipGraph(
        IReadOnlyList<CollectionMetadata> collections,
        IReadOnlyDictionary<string, Type> collectionTypes)
    {
        _collections = collections;
        var known = collections.Select(c => c.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var map = new Dictionary<string, IReadOnlyList<RelationDescriptor>>(StringComparer.OrdinalIgnoreCase);
        var inboundRestrict = new Dictionary<string, List<(string, string)>>(StringComparer.OrdinalIgnoreCase);
        var inboundSetNull = new Dictionary<string, List<(string, string)>>(StringComparer.OrdinalIgnoreCase);
        var inboundCascade = new Dictionary<string, List<(string, string)>>(StringComparer.OrdinalIgnoreCase);

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

                // Build the inbound OnDelete indexes (Restrict/SetNull/Cascade) for delete-guard /
                // purge. Only M2O relations carry a real FK on the source side.
                if (r.Kind == RelationKind.ManyToOne)
                {
                    var bucket = r.OnDelete switch
                    {
                        OnDelete.SetNull => inboundSetNull,
                        OnDelete.Cascade => inboundCascade,
                        _ => inboundRestrict
                    };
                    if (!bucket.TryGetValue(r.TargetCollection, out var lst))
                        bucket[r.TargetCollection] = lst = [];
                    lst.Add((c.Name, r.ForeignKey!));
                }
            }

            map[c.Name] = descriptors;
        }

        _byCollection = map;
        _inboundRestrict = Freeze(inboundRestrict);
        _inboundSetNull = Freeze(inboundSetNull);
        _inboundCascade = Freeze(inboundCascade);
    }

    private static IReadOnlyDictionary<string, IReadOnlyList<(string, string)>> Freeze(
        Dictionary<string, List<(string, string)>> source) =>
        source.ToDictionary(
            k => k.Key,
            v => (IReadOnlyList<(string, string)>)v.Value,
            StringComparer.OrdinalIgnoreCase);

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

    public IReadOnlyList<(string SourceCollection, string ForeignKey)> InboundSetNull(
        string targetCollection) =>
        _inboundSetNull.TryGetValue(targetCollection, out var l) ? l : [];

    public IReadOnlyList<(string SourceCollection, string ForeignKey)> InboundCascade(
        string targetCollection) =>
        _inboundCascade.TryGetValue(targetCollection, out var l) ? l : [];

    // ── Infrastructure-only descriptor access ────────────────────────────────

    public IReadOnlyList<RelationDescriptor> Descriptors(string collection) =>
        _byCollection.TryGetValue(collection, out var d) ? d : [];

    // ── IM2MDescriptorSource ─────────────────────────────────────────────────

    public IReadOnlyList<M2MDescriptor> M2MDescriptors(string collection) =>
        Descriptors(collection)
            .Where(d => d.Meta.Kind == RelationKind.ManyToMany
                        && d.JunctionType is not null
                        && d.JunctionParentFk is not null
                        && d.JunctionTargetFk is not null)
            .Select(d => new M2MDescriptor(
                d.Meta.Name,
                d.Meta.TargetCollection,
                d.JunctionType!,
                d.JunctionParentFk!,
                d.JunctionTargetFk!,
                d.JunctionSort,
                d.JunctionCollection,
                d.JunctionPayload))
            .ToList();

    /// <summary>
    /// Walks every known collection's own M2M descriptors and keeps the ones whose
    /// <see cref="M2MDescriptor.TargetCollection"/> matches <paramref name="targetCollection"/> —
    /// i.e. the junction rows purging <paramref name="targetCollection"/> must also clean up, even
    /// though the relation is declared on a DIFFERENT ("owning") collection.
    /// </summary>
    public IReadOnlyList<InboundM2MDescriptor> InboundM2MDescriptors(string targetCollection) =>
        _byCollection
            .SelectMany(kv => kv.Value
                .Where(d => d.Meta.Kind == RelationKind.ManyToMany
                            && d.JunctionType is not null
                            && d.JunctionParentFk is not null
                            && d.JunctionTargetFk is not null
                            && string.Equals(d.Meta.TargetCollection, targetCollection, StringComparison.OrdinalIgnoreCase))
                .Select(d => new InboundM2MDescriptor(kv.Key, new M2MDescriptor(
                    d.Meta.Name,
                    d.Meta.TargetCollection,
                    d.JunctionType!,
                    d.JunctionParentFk!,
                    d.JunctionTargetFk!,
                    d.JunctionSort,
                    d.JunctionCollection,
                    d.JunctionPayload))))
            .ToList();

    // ── Helpers ─────────────────────────────────────────────────────────────

    private RelationDescriptor BuildDescriptor(Type entityType, RelationMetadata r)
    {
        // Find the nav property by matching camelCase name
        var prop = entityType.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .First(p => string.Equals(
                System.Text.Json.JsonNamingPolicy.CamelCase.ConvertName(p.Name),
                r.Name,
                StringComparison.OrdinalIgnoreCase));

        var navData = prop.CustomAttributes
            .First(a => a.AttributeType == typeof(Navigate));

        if (r.Kind == RelationKind.ManyToMany)
        {
            var junctionType = MetadataScanner.NavigateMappingType(navData)!;
            var parentFk = MetadataScanner.NavigateMappingA(navData)!;
            var targetFk = MetadataScanner.NavigateMappingB(navData)!;
            var sort = prop.GetCustomAttribute<Struo.Domain.Metadata.Attributes.CmsRelationAttribute>()?.SortField;
            var (junctionCollection, payload) = JunctionPayloadOf(junctionType, parentFk, targetFk, sort);
            return new RelationDescriptor(r, junctionType, parentFk, targetFk, sort, null, junctionCollection, payload);
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

    // Single source of the junction payload field list: everything downstream that needs to know which
    // extra columns a M2M junction collection carries (beyond its two FKs and its sort column) reads
    // M2MDescriptor.JunctionPayload rather than re-deriving this itself.
    private (string? Collection, IReadOnlyList<JunctionPayloadField>? Payload) JunctionPayloadOf(
        Type junctionType, string parentFk, string targetFk, string? sortProperty)
    {
        var name = System.Text.Json.JsonNamingPolicy.CamelCase.ConvertName(junctionType.Name);
        var meta = _collections.FirstOrDefault(c => string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase));
        if (meta is null) return (null, null);

        var excluded = new HashSet<string>(StringComparer.Ordinal) { parentFk, targetFk };
        if (sortProperty is not null) excluded.Add(sortProperty);

        var props = junctionType.GetProperties(BindingFlags.Public | BindingFlags.Instance);
        var payload = meta.Fields
            .Where(f => !f.IsSystem && !f.ReadOnly)
            .Select(f => (Field: f, Prop: props.FirstOrDefault(p =>
                string.Equals(System.Text.Json.JsonNamingPolicy.CamelCase.ConvertName(p.Name), f.Name, StringComparison.OrdinalIgnoreCase))))
            .Where(x => x.Prop is not null && !excluded.Contains(x.Prop!.Name))
            .Select(x => new JunctionPayloadField(x.Field.Name, x.Prop!.Name, x.Field.Hidden))
            .ToList();
        return (meta.Name, payload);
    }
}
