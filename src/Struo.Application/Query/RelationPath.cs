// src/Struo.Application/Query/RelationPath.cs
using Struo.Application.Metadata;
using Struo.Domain.Metadata.Enums;
using Struo.Domain.Metadata.Models;
using Struo.Domain.Query;

namespace Struo.Application.Query;

/// <summary>One relation hop in a dotted path, plus the collection it is declared on.</summary>
public sealed record RelationSegment(string RelationName, RelationMetadata Relation, string DeclaringCollection);

/// <summary>
/// A validated dotted query path: a sequence of relation segments ending in a scalar leaf
/// field on the terminal collection. Built and validated against the relationship graph and
/// collection metadata; throws <see cref="QueryException"/> on any invalid segment, unknown
/// leaf, or over-depth path.
/// </summary>
public sealed class RelationPath
{
    public IReadOnlyList<RelationSegment> Segments { get; }
    public string LeafField { get; }
    public string TerminalCollection { get; }

    /// <summary>True iff every segment is to-one (M2O) — the only paths that can be sorted on.</summary>
    public bool IsSortable => Segments.All(s => s.Relation.Kind == RelationKind.ManyToOne);

    private RelationPath(IReadOnlyList<RelationSegment> segments, string leafField, string terminalCollection)
    {
        Segments = segments;
        LeafField = leafField;
        TerminalCollection = terminalCollection;
    }

    public static bool IsRelationPath(string path) => path.Contains('.');

    public static RelationPath Parse(
        string rootCollection, string path,
        IRelationshipGraph graph, IMetadataProvider metadata, int maxDepth)
    {
        var parts = path.Split('.');
        if (parts.Length < 2)
            throw new QueryException($"'{path}' is not a relation path.");

        var relCount = parts.Length - 1;          // last part is the leaf field
        if (relCount > maxDepth)
            throw new QueryException(
                $"Relation path '{path}' exceeds the maximum depth of {maxDepth}.");

        var segments = new List<RelationSegment>(relCount);
        var current = rootCollection;
        for (var i = 0; i < relCount; i++)
        {
            var relName = parts[i];
            var rel = graph.Resolve(current, relName)
                ?? throw new QueryException(
                    $"Unknown relation '{relName}' on '{current}' in path '{path}'.");
            segments.Add(new RelationSegment(relName, rel, current));
            current = rel.TargetCollection;
        }

        var leaf = parts[^1];
        var terminal = metadata.GetCollection(current)
            ?? throw new QueryException($"Unknown collection '{current}' in path '{path}'.");
        var leafKnown =
            string.Equals(leaf, "id", StringComparison.OrdinalIgnoreCase) ||
            terminal.Fields.Any(f => !f.Hidden && string.Equals(f.Name, leaf, StringComparison.OrdinalIgnoreCase));
        if (!leafKnown)
            throw new QueryException(
                $"Unknown field '{leaf}' on collection '{current}' in path '{path}'.");

        return new RelationPath(segments, leaf, current);
    }
}
