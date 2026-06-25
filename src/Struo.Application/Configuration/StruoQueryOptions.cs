namespace Struo.Application.Configuration;

public sealed class StruoQueryOptions
{
    public const string SectionName = "Query";
    public int MaxLimit { get; set; } = 100;
    public int DefaultLimit { get; set; } = 25;
    public int MaxFilterConditions { get; set; } = 50;
}
