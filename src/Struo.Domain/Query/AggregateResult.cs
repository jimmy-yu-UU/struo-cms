namespace Struo.Domain.Query;

public sealed record AggregateResult(IReadOnlyDictionary<AggregateOp, IReadOnlyDictionary<string, object?>> Values);
