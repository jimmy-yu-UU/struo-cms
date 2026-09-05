// src/Struo.Application/Query/RelationQuantifierFolder.cs
using Struo.Domain.Query;

namespace Struo.Application.Query;

/// <summary>Folds the flat query-string conditions into quantifier predicates: every condition whose
/// path contains a `_some`/`_none` segment is grouped by (prefix before the first quantifier, quantifier)
/// into ONE RelationPredicateFilter whose Inner is the AND of the remaining paths, recursively.</summary>
public static class RelationQuantifierFolder
{
    public static IReadOnlyList<FilterNode> Fold(IReadOnlyList<ComparisonFilter> flat)
    {
        var result = new List<FilterNode>();
        // Prefix is grouped case-insensitively: `filter[Tags._some.name]` and
        // `filter[tags._some.color]` refer to the same relation and must fold into ONE
        // each-exists predicate, not two. The key's casing (used for the emitted
        // RelationPredicateFilter's Path) is whichever variant is seen first, since
        // Dictionary/List preserve the key object passed to the first Add.
        var groups = new Dictionary<(string Prefix, RelationQuantifier Q), List<ComparisonFilter>>(PrefixKeyComparer.Instance);
        var order = new List<(string, RelationQuantifier)>();

        foreach (var c in flat)
        {
            var split = SplitAtFirstQuantifier(c.FieldPath);
            if (split is null) { result.Add(c); continue; }
            var (prefix, q, rest) = split.Value;
            var key = (prefix, q);
            if (!groups.TryGetValue(key, out var list)) { groups[key] = list = []; order.Add(key); }
            list.Add(c with { FieldPath = rest });
        }

        foreach (var key in order)
        {
            var inner = Fold(groups[key]);
            result.Add(new RelationPredicateFilter(key.Item1, key.Item2, inner.Count == 1 ? inner[0] : new LogicalFilter(LogicalOperator.And, inner)));
        }
        return result;
    }

    private static (string Prefix, RelationQuantifier Q, string Tail)? SplitAtFirstQuantifier(string path)
    {
        var parts = path.Split('.');
        for (var i = 0; i < parts.Length; i++)
        {
            if (!FilterReservedTokens.TryQuantifier(parts[i], out var q)) continue;
            if (i == 0)
                throw new QueryException($"'{path}': '{parts[i]}' must follow a relation name.");
            if (i == parts.Length - 1)
                throw new QueryException($"'{path}': '{parts[i]}' must be followed by a condition on the related collection.");
            if (FilterReservedTokens.TryQuantifier(parts[i + 1], out _))
                throw new QueryException($"'{path}': two quantifiers in a row are not allowed.");
            return (string.Join('.', parts[..i]), q, string.Join('.', parts[(i + 1)..]));
        }
        return null;
    }

    /// <summary>Groups by (Prefix, Quantifier) with the Prefix compared <see cref="StringComparer.OrdinalIgnoreCase"/>,
    /// so `Tags._some` and `tags._some` fold into one group instead of two.</summary>
    private sealed class PrefixKeyComparer : IEqualityComparer<(string Prefix, RelationQuantifier Q)>
    {
        public static readonly PrefixKeyComparer Instance = new();

        public bool Equals((string Prefix, RelationQuantifier Q) x, (string Prefix, RelationQuantifier Q) y) =>
            x.Q == y.Q && string.Equals(x.Prefix, y.Prefix, StringComparison.OrdinalIgnoreCase);

        public int GetHashCode((string Prefix, RelationQuantifier Q) obj) =>
            HashCode.Combine(obj.Prefix.ToUpperInvariant(), obj.Q);
    }
}
