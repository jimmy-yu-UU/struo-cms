namespace Struo.Application.Changes;

/// <summary>Collects one write operation's changes: insertion-ordered, deduplicated by
/// (collection, id) case-insensitively, with <see cref="ItemChangeKind.Purged"/> winning over any
/// other kind for the same item (a purged row may first have been reached as a SetNull target).</summary>
public sealed class ItemChangeSet
{
    private readonly Dictionary<string, int> index = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<ItemChange> changes = [];

    public int Count => changes.Count;

    public void Add(string collection, string id, ItemChangeKind kind)
    {
        var key = collection + "/" + id;
        if (index.TryGetValue(key, out var i))
        {
            if (kind == ItemChangeKind.Purged && changes[i].Kind != ItemChangeKind.Purged)
                changes[i] = changes[i] with { Kind = ItemChangeKind.Purged };
            return;
        }
        index[key] = changes.Count;
        changes.Add(new ItemChange(collection, id, kind));
    }

    public IReadOnlyList<ItemChange> ToList() => changes.ToList();
}
