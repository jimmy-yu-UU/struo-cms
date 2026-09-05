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
}
