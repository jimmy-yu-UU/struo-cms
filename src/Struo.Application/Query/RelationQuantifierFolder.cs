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
        var groups = new Dictionary<(string Prefix, RelationQuantifier Q), List<ComparisonFilter>>();
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
}
