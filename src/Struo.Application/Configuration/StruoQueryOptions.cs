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

    [Range(1, int.MaxValue)]
    public int MaxFacets { get; set; } = 10;

    [Range(1, int.MaxValue)]
    public int MaxFacetValues { get; set; } = 50;

    [Range(1, int.MaxValue)]
    public int MaxAggregates { get; set; } = 10;

    /// <summary>Upper bound on the candidate ids an <c>ISearchProvider</c> may return for one request; more is a provider contract violation (500), never a truncation.</summary>
    [Range(1, int.MaxValue)]
    public int MaxSearchCandidates { get; set; } = 1000;
}
