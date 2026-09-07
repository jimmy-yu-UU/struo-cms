using Microsoft.Extensions.Logging;
using Struo.Application.Changes;

namespace Struo.Infrastructure.Changes;

/// <summary>
/// Fans one write's change batch out to every registered <see cref="IItemChangeListener"/> in
/// registration order. Each listener is isolated: a throwing listener is logged at Error (type name,
/// batch size, per-kind summary) and the next one still runs. Never throws — callers have already
/// committed, and a listener failure must not turn a successful write into an error response.
/// Lives in Infrastructure because Struo.Application has no logging dependency.
/// </summary>
public sealed class ItemChangeNotifier(IEnumerable<IItemChangeListener> listeners, ILogger<ItemChangeNotifier> logger) : IItemChangeNotifier
{
    private readonly IReadOnlyList<IItemChangeListener> listeners = listeners.ToList();

    public async Task NotifyAsync(IReadOnlyList<ItemChange> changes, CancellationToken ct = default)
    {
        if (changes.Count == 0 || listeners.Count == 0) return;
        foreach (var listener in listeners)
        {
            try
            {
                await listener.OnChangedAsync(changes, ct);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Item change listener {Listener} failed for {Count} change(s): {Summary}",
                    listener.GetType().FullName, changes.Count, Summarize(changes));
            }
        }
    }

    private static string Summarize(IReadOnlyList<ItemChange> changes) =>
        string.Join(", ", changes.GroupBy(c => c.Kind).OrderBy(g => g.Key).Select(g => $"{g.Key}={g.Count()}"));
}
