namespace Struo.Domain.Query;

public sealed record FacetBucket(object? Value, long Count);

public sealed record FacetResult(string Field, IReadOnlyList<FacetBucket> Values);
