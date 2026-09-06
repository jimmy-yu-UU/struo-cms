namespace Struo.Infrastructure.Query;

public sealed class FacetRow<TValue>
{
    public TValue? Value { get; set; }
    public long Count { get; set; }
}

public sealed class AggregateRow
{
    public const int SlotCount = 10;

    public object? V0 { get; set; }
    public object? V1 { get; set; }
    public object? V2 { get; set; }
    public object? V3 { get; set; }
    public object? V4 { get; set; }
    public object? V5 { get; set; }
    public object? V6 { get; set; }
    public object? V7 { get; set; }
    public object? V8 { get; set; }
    public object? V9 { get; set; }

    public object? Slot(int i) => i switch
    {
        0 => V0, 1 => V1, 2 => V2, 3 => V3, 4 => V4, 5 => V5, 6 => V6, 7 => V7, 8 => V8, 9 => V9,
        _ => throw new ArgumentOutOfRangeException(nameof(i)),
    };
}
