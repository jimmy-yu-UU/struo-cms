namespace Struo.Domain.Query;

public sealed record AggregateSpec(IReadOnlyDictionary<AggregateOp, IReadOnlyList<string>> Fields);
