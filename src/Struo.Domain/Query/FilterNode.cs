// src/Struo.Domain/Query/FilterNode.cs
namespace Struo.Domain.Query;

public abstract record FilterNode;

public sealed record LogicalFilter(LogicalOperator Op, IReadOnlyList<FilterNode> Children) : FilterNode;

public sealed record ComparisonFilter(string FieldPath, QueryOperator Op, object? Value) : FilterNode;
