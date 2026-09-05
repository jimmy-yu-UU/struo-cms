// src/Struo.Application/Query/RelationPath.cs
using Struo.Application.Metadata;
using Struo.Domain.Metadata.Enums;
using Struo.Domain.Metadata.Models;
using Struo.Domain.Query;

namespace Struo.Application.Query;

/// <summary>One relation hop in a dotted path, plus the collection it is declared on.</summary>
public sealed record RelationSegment(string RelationName, RelationMetadata Relation, string DeclaringCollection);

/// <summary>
/// A validated dotted query path: a sequence of relation segments ending either in a scalar leaf
/// field on the terminal collection, in a <c>_junction</c> pseudo-segment naming a payload field on
/// an M2M junction collection, or (via <see cref="ParseRelationOnly"/>) in no leaf at all. Built and
/// validated against the relationship graph and collection metadata; throws
/// <see cref="QueryException"/> on any invalid segment, unknown leaf, or over-depth path.
/// </summary>
public sealed class RelationPath
{
    public IReadOnlyList<RelationSegment> Segments { get; }
    public string LeafField { get; }
    public string TerminalCollection { get; }

    /// <summary>True when <see cref="LeafField"/> is a payload field on an M2M junction collection
    /// reached via a <c>._junction.</c> pseudo-segment, rather than a field on <see cref="TerminalCollection"/>.</summary>
    public bool IsJunctionLeaf { get; }

    /// <summary>The junction collection <see cref="LeafField"/> belongs to when <see cref="IsJunctionLeaf"/>; null otherwise.</summary>
    public string? JunctionCollection { get; }

    /// <summary>True iff every segment is to-one (M2O) — the only paths that can be sorted on.</summary>
    public bool IsSortable => Segments.All(s => s.Relation.Kind == RelationKind.ManyToOne);

    private RelationPath(
        IReadOnlyList<RelationSegment> segments, string leafField, string terminalCollection,
        bool isJunctionLeaf, string? junctionCollection)
    {
        Segments = segments;
        LeafField = leafField;
        TerminalCollection = terminalCollection;
        IsJunctionLeaf = isJunctionLeaf;
        JunctionCollection = junctionCollection;
    }

    public static bool IsRelationPath(string path) => path.Contains('.');

    /// <summary>
    /// Parses a dotted path rooted at <paramref name="rootCollection"/> ending in a leaf: either a
    /// scalar field on the terminal collection, or (when the second-to-last segment is
    /// <c>_junction</c>) a payload field on the preceding M2M relation's junction collection.
    /// </summary>
    /// <param name="maxDepth">The remaining hop budget to check against (the configured limit minus
    /// hops already consumed by an enclosing relation predicate); may be less than the configured
    /// limit.</param>
    /// <param name="configuredMaxDepth">The limit to report in the depth-exceeded message. Defaults
    /// to <paramref name="maxDepth"/> for callers that pass the full configured limit as
    /// <paramref name="maxDepth"/>; a caller that narrows <paramref name="maxDepth"/> by hops already
    /// used (see <c>QueryValidator</c>) passes the true configured limit here so the message states
    /// the limit the caller configured, not the remaining budget.</param>
    public static RelationPath Parse(
        string rootCollection, string path,
        IRelationshipGraph graph, IMetadataProvider metadata, int maxDepth, int? configuredMaxDepth = null)
    {
        var reportedMaxDepth = configuredMaxDepth ?? maxDepth;
        var parts = path.Split('.');
        if (parts.Length < 2)
            throw new QueryException($"'{path}' is not a relation path.");
        if (parts.Any(p => FilterReservedTokens.TryQuantifier(p, out _)))
            throw new QueryException($"'{path}': relation quantifiers are not valid inside a field path here.");

        var junctionIdx = Array.IndexOf(parts, FilterReservedTokens.Junction);
        if (junctionIdx >= 0)
            return ParseJunctionLeaf(new ParseContext(rootCollection, path, parts, graph, metadata), junctionIdx, maxDepth, reportedMaxDepth);

        var relCount = parts.Length - 1;          // last part is the leaf field
        if (relCount > maxDepth)
            throw new QueryException(
                $"Relation path '{path}' exceeds the maximum depth of {reportedMaxDepth}.");

        var (segments, current) = WalkSegments(rootCollection, parts, relCount, graph, path);

        var leaf = parts[^1];
        var terminal = metadata.GetCollection(current)
            ?? throw new QueryException($"Unknown collection '{current}' in path '{path}'.");
        var leafKnown =
            string.Equals(leaf, "id", StringComparison.OrdinalIgnoreCase) ||
            terminal.Fields.Any(f => !f.Hidden && string.Equals(f.Name, leaf, StringComparison.OrdinalIgnoreCase));
        if (!leafKnown)
            throw new QueryException(
                $"Unknown field '{leaf}' on collection '{current}' in path '{path}'.");

        return new RelationPath(segments, leaf, current, isJunctionLeaf: false, junctionCollection: null);
    }

    /// <summary>
    /// Parses a dotted path that is a pure relation prefix — every segment is a relation hop, with
    /// no leaf field. Used by a quantified relation predicate (<c>_some</c>/<c>_none</c>), whose
    /// <c>RelationPath</c> names only the hops to the collection the predicate's inner filter runs
    /// against.
    /// </summary>
    public static RelationPath ParseRelationOnly(
        string rootCollection, string path,
        IRelationshipGraph graph, IMetadataProvider metadata, int maxDepth, int? configuredMaxDepth = null)
    {
        var parts = path.Split('.');
        var relCount = parts.Length;
        if (relCount > maxDepth)
            throw new QueryException(
                $"Relation path '{path}' exceeds the maximum depth of {configuredMaxDepth ?? maxDepth}.");

        var (segments, current) = WalkSegments(rootCollection, parts, relCount, graph, path);
        _ = metadata.GetCollection(current)
            ?? throw new QueryException($"Unknown collection '{current}' in path '{path}'.");

        return new RelationPath(segments, string.Empty, current, isJunctionLeaf: false, junctionCollection: null);
    }

    // Bundles the inputs shared by every path-parsing helper below (root collection, raw path, its
    // dotted parts, and the graph/metadata to resolve segments and leaves against) into one value, so
    // that helpers needing several of them plus their own numeric parameters stay within the
    // parameter-count limit without changing behaviour.
    private readonly record struct ParseContext(
        string RootCollection, string Path, string[] Parts, IRelationshipGraph Graph, IMetadataProvider Metadata);

    private static RelationPath ParseJunctionLeaf(ParseContext ctx, int junctionIdx, int maxDepth, int configuredMaxDepth)
    {
        if (junctionIdx != ctx.Parts.Length - 2)
            throw new QueryException($"'{ctx.Path}': '_junction' must be followed by exactly one junction field.");

        var relCount = junctionIdx;               // hops before "_junction"; the leaf does not count
        if (relCount > maxDepth)
            throw new QueryException(
                $"Relation path '{ctx.Path}' exceeds the maximum depth of {configuredMaxDepth}.");

        var (segments, _) = WalkSegments(ctx.RootCollection, ctx.Parts, relCount, ctx.Graph, ctx.Path);
        var lastRelation = relCount > 0 ? segments[^1].Relation : null;
        if (lastRelation is null || lastRelation.Kind != RelationKind.ManyToMany || lastRelation.JunctionCollection is null)
            throw new QueryException(
                $"'{ctx.Path}': '_junction' is only valid after a many-to-many relation with a junction collection.");

        var junctionCollection = lastRelation.JunctionCollection;
        var junctionMeta = ctx.Metadata.GetCollection(junctionCollection)
            ?? throw new QueryException($"Unknown collection '{junctionCollection}' in path '{ctx.Path}'.");

        var leaf = ctx.Parts[^1];
        var leafKnown = junctionMeta.Fields.Any(f => !f.Hidden && string.Equals(f.Name, leaf, StringComparison.OrdinalIgnoreCase));
        if (!leafKnown)
            throw new QueryException(
                $"Unknown field '{leaf}' on collection '{junctionCollection}' in path '{ctx.Path}'.");

        return new RelationPath(segments, leaf, junctionCollection, isJunctionLeaf: true, junctionCollection: junctionCollection);
    }

    /// <summary>
    /// Walks the first <paramref name="count"/> segments of <paramref name="parts"/> as relation hops
    /// starting from <paramref name="rootCollection"/>, resolving each against <paramref name="graph"/>.
    /// Shared by <see cref="Parse"/> (which stops before a trailing leaf) and
    /// <see cref="ParseRelationOnly"/> (which walks every segment).
    /// </summary>
    private static (List<RelationSegment> Segments, string Current) WalkSegments(
        string rootCollection, string[] parts, int count, IRelationshipGraph graph, string path)
    {
        var segments = new List<RelationSegment>(count);
        var current = rootCollection;
        for (var i = 0; i < count; i++)
        {
            var relName = parts[i];
            var rel = graph.Resolve(current, relName)
                ?? throw new QueryException(
                    $"Unknown relation '{relName}' on '{current}' in path '{path}'.");
            segments.Add(new RelationSegment(relName, rel, current));
            current = rel.TargetCollection;
        }
        return (segments, current);
    }
}
