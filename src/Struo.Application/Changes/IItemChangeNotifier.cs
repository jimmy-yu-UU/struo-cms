namespace Struo.Application.Changes;

/// <summary>Core-internal dispatcher ItemService/FileService depend on; the Infrastructure
/// implementation fans out to every registered <see cref="IItemChangeListener"/>.</summary>
public interface IItemChangeNotifier
{
    Task NotifyAsync(IReadOnlyList<ItemChange> changes, CancellationToken ct = default);
}
