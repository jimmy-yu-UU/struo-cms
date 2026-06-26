// src/Struo.Domain/Query/QueryModel.cs
namespace Struo.Domain.Query;

public sealed record QueryModel(
    IReadOnlyList<string>? Fields,
    FilterNode? Filter,
    IReadOnlyList<SortField> Sort,
    int Limit,
    int Offset,
    string? Search)
{
    /// <summary>Optional read-time relation expansion request.</summary>
    public DeepSpec? Deep { get; init; }
}
