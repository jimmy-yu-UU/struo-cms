// src/Struo.Application/Query/FacetFilterPruner.cs
using Struo.Domain.Query;

namespace Struo.Application.Query;

/// <summary>
/// Removes, from a filter tree, every condition that already constrains the field a facet is being
/// computed for — the "disjunctive" facet counts (how many rows would match EACH candidate value of
/// this facet, holding every other filter constant) must not also be narrowed by the facet's own
/// current selection, or picking a value would make every other value's count collapse toward zero.
/// Pure: never mutates the input tree, only builds a smaller one (or returns the same node/subtree
/// unchanged when nothing needed pruning).
/// </summary>
public static class FacetFilterPruner
{
    public static FilterNode? Prune(FilterNode? filter, ResolvedFacetPath facet) => filter switch
    {
        null => null,
        ComparisonFilter c => Matches(c.FieldPath, facet) ? null : c,
        RelationPredicateFilter p => facet.Relation is not null && MatchesRelation(p.RelationPath, facet.Relation.Name) ? null : p,
        LogicalFilter l => PruneGroup(l, facet),
        _ => filter,
    };

    private static FilterNode? PruneGroup(LogicalFilter l, ResolvedFacetPath facet)
    {
        var prunedChildren = l.Children.Select(ch => Prune(ch, facet)).ToList();
        var kept = prunedChildren.Where(ch => ch is not null).Cast<FilterNode>().ToList();
        return kept.Count switch
        {
            0 => null,
            1 => kept[0],
            _ => IsUnchanged(l.Children, prunedChildren) ? l : new LogicalFilter(l.Op, kept),
        };
    }

    // A group is unchanged only when every child pruned back to the exact same reference it started
    // as — not merely when the surviving COUNT matches, since a nested group can shed some of its own
    // children (staying non-null, so still "kept" here) without losing any top-level sibling.
    private static bool IsUnchanged(IReadOnlyList<FilterNode> original, IReadOnlyList<FilterNode?> pruned)
    {
        if (original.Count != pruned.Count) return false;
        for (var i = 0; i < original.Count; i++)
            if (!ReferenceEquals(original[i], pruned[i])) return false;
        return true;
    }

    private static bool Matches(string fieldPath, ResolvedFacetPath facet)
    {
        if (facet.Kind == FacetPathKind.OwnField)
            return string.Equals(fieldPath, facet.OwnField, StringComparison.OrdinalIgnoreCase);
        var rel = facet.Relation!;
        if (rel.ForeignKey is not null && string.Equals(fieldPath, rel.ForeignKey, StringComparison.OrdinalIgnoreCase)) return true;
        return MatchesRelation(fieldPath, rel.Name);
    }

    private static bool MatchesRelation(string path, string relationName) =>
        string.Equals(path, relationName, StringComparison.OrdinalIgnoreCase)
        || path.StartsWith(relationName + ".", StringComparison.OrdinalIgnoreCase);
}
