// src/Struo.Application/Query/ResolvedIdSetGuard.cs
using Struo.Domain.Query;

namespace Struo.Application.Query;

/// <summary>
/// Bounds the intermediate id sets produced while rewriting a filter into an own-collection
/// <c>id IN (...)</c> condition — the cross-relation (dotted) filter walk in
/// <c>RelationFilterResolver</c> and the translatable-field search union in
/// <c>SqlSugarItemRepository</c>. Both fully materialize ids, and <c>QueryValidator</c>'s caps bound
/// the RESULT page rather than these intermediate sets, so before this guard a wide condition cost
/// O(table) memory plus one enormous SQL statement.
/// <para>
/// Deliberately a hard failure rather than a truncation: a shortened id set silently drops matching
/// rows, and returning quietly wrong results is worse than refusing the query. <see cref="QueryException"/>
/// maps to 400 <c>BAD_USER_INPUT</c>, which is the right shape — the caller's condition is too wide,
/// the server is not broken.
/// </para>
/// </summary>
public static class ResolvedIdSetGuard
{
    /// <summary>
    /// Returns <paramref name="ids"/> unchanged when it is within <paramref name="max"/>, otherwise
    /// throws. <paramref name="step"/> names the resolution step for the error message (e.g.
    /// <c>"category.name"</c>) so a caller with several dotted filters can tell which one was too wide.
    /// </summary>
    public static IReadOnlyList<object> Ensure(IReadOnlyList<object> ids, int max, string step)
    {
        if (ids.Count > max)
            throw new QueryException(
                $"Resolving '{step}' matched too many rows ({ids.Count}, limit {max}). " +
                "Narrow the filter or search term, or raise Query:MaxResolvedFilterIds.");
        return ids;
    }

    /// <summary>Count-only overload for callers accumulating into a set rather than a list.</summary>
    public static void EnsureCount(int count, int max, string step)
    {
        if (count > max)
            throw new QueryException(
                $"Resolving '{step}' matched too many rows ({count}, limit {max}). " +
                "Narrow the filter or search term, or raise Query:MaxResolvedFilterIds.");
    }
}
