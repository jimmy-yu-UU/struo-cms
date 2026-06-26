// src/Struo.Application/Query/IRelationFilterResolver.cs
using Struo.Domain.Query;

namespace Struo.Application.Query;

/// <summary>
/// Rewrites every dotted (cross-relation) <see cref="ComparisonFilter"/> in a filter tree into
/// an own-collection <c>id IN (…)</c> condition (or <c>id IS NULL</c> for an empty match) by
/// resolving the relation path to a set of root ids. Non-dotted nodes and logical structure
/// pass through unchanged, preserving single-level <c>_and</c>/<c>_or</c> composition.
/// </summary>
public interface IRelationFilterResolver
{
    Task<FilterNode?> RewriteAsync(string rootCollection, FilterNode? filter, CancellationToken ct = default);
}
