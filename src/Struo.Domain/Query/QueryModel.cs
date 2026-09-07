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

    public IReadOnlyList<string>? Facets { get; init; }
    public AggregateSpec? Aggregate { get; init; }

    /// <summary>
    /// Set by <c>SearchCandidateResolver</c> when a registered <c>ISearchProvider</c> answered this
    /// request's <see cref="Search"/>: the candidate root ids, already parsed to the PK CLR type and
    /// deduplicated. Non-null replaces the built-in LIKE search with <c>id IN (…)</c> (an empty list
    /// matches nothing); null means "no provider answered" and <see cref="Search"/> is used as before.
    /// </summary>
    public IReadOnlyList<object>? SearchCandidates { get; init; }
}
