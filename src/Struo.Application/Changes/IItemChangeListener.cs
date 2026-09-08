namespace Struo.Application.Changes;

/// <summary>
/// The write-side seam a fork implements to react to content writes (index synchronisation,
/// webhooks, cache invalidation). Called once per write operation, AFTER the transaction committed,
/// with every item that operation touched (a purge lists each cascaded row and each row whose FK was
/// set to null). Best-effort: an exception is logged at Error and never affects the write's result;
/// there is no retry — a fork needing durability queues inside its listener. Register any number:
/// <c>services.AddScoped&lt;IItemChangeListener, MyIndexer&gt;()</c>. Not raised for identity
/// collections written through their own stores, for site settings, or for M2M inverse sides.
/// </summary>
public interface IItemChangeListener
{
    Task OnChangedAsync(IReadOnlyList<ItemChange> changes, CancellationToken ct = default);
}
