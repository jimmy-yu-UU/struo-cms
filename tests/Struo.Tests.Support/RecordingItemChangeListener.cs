using Struo.Application.Changes;

namespace Struo.Tests.Support;

/// <summary>Test double: records every call (one entry per OnChangedAsync) and can run a scripted callback.</summary>
public sealed class RecordingItemChangeListener : IItemChangeListener
{
    public List<IReadOnlyList<ItemChange>> Calls { get; } = [];
    public IEnumerable<ItemChange> All => Calls.SelectMany(c => c);
    public Func<IReadOnlyList<ItemChange>, Task>? OnCall { get; init; }

    public async Task OnChangedAsync(IReadOnlyList<ItemChange> changes, CancellationToken ct = default)
    {
        Calls.Add(changes);
        if (OnCall is not null) await OnCall(changes);
    }
}
