// src/Struo.Domain/Query/FilterNode.cs
namespace Struo.Domain.Query;

public abstract record FilterNode;

public sealed record LogicalFilter(LogicalOperator Op, IReadOnlyList<FilterNode> Children) : FilterNode;

public sealed record ComparisonFilter(string FieldPath, QueryOperator Op, object? Value) : FilterNode;

/// <summary>A quantified relation predicate: <see cref="RelationPath"/> is a dotted path of relation
/// segments only (no leaf field) rooted at the current collection; <see cref="Inner"/> is a complete
/// filter rooted at the path's terminal collection. Some = at least one related row satisfies Inner;
/// None = no related row does (a parent with no related rows satisfies None).</summary>
public sealed record RelationPredicateFilter(string RelationPath, RelationQuantifier Quantifier, FilterNode Inner) : FilterNode;
