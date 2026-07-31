using System.ComponentModel.DataAnnotations;

namespace Struo.Application.Configuration;

public sealed class StruoQueryOptions
{
    public const string SectionName = "Query";

    [Range(1, int.MaxValue)]
    public int MaxLimit { get; set; } = 100;

    [Range(1, int.MaxValue)]
    public int DefaultLimit { get; set; } = 25;

    [Range(1, int.MaxValue)]
    public int MaxFilterConditions { get; set; } = 50;

    [Range(1, int.MaxValue)]
    public int MaxRelationDepth { get; set; } = 6;

    /// <summary>
    /// Upper bound on how many ids a single filter-resolution step may materialize. A cross-relation
    /// (dotted) filter and a translatable-field search are both answered by resolving the condition to
    /// a set of root ids and rewriting it into an own-collection <c>id IN (...)</c>; every leaf lookup
    /// and every walk-back hop is bounded by this value. Without it a deliberately wide condition
    /// (<c>?filter[category.name][_contains]=a</c>, <c>?search=a</c>) costs O(table) memory plus one
    /// enormous SQL statement, reachable by any caller with a read grant — the other caps here bound
    /// the RESULT page, not this intermediate set.
    /// <para>
    /// Exceeding it raises a <c>QueryException</c> (400, "narrow the filter"). It never truncates:
    /// a silently shortened id set returns quietly wrong rows, which is worse than refusing. Raise it
    /// if a fork's legitimate filters resolve to larger sets — the cost is memory plus SQL statement
    /// size, roughly 40 bytes of statement text per uuid.
    /// </para>
    /// </summary>
    [Range(1, int.MaxValue)]
    public int MaxResolvedFilterIds { get; set; } = 5000;
}
