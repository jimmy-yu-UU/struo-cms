namespace Struo.Application.Changes;

/// <summary>What happened to an item, as seen after the write committed. Revert is an Updated; a
/// no-op trash/restore (already in that state) raises nothing.</summary>
public enum ItemChangeKind { Created, Updated, Trashed, Restored, Purged }
