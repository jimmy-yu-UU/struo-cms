namespace Struo.Application.Security;

/// <summary>
/// The caller's resolved permissions for the current request. Super-admin short-circuits every
/// check; otherwise a per-collection grant is consulted and an absent collection is denied. A hidden
/// junction collection (<see cref="RbacCollections.GoverningCollection"/>) answers with the grant of
/// the collection that owns it: read follows read, and write and delete both follow write.
/// </summary>
public sealed class EffectivePermissions(
    bool isSuperAdmin,
    IReadOnlyDictionary<string, (bool Read, bool Write, bool Delete)> byCollection)
{
    /// <summary>Empty snapshot: not super-admin, no grants (denies everything). The safe default.</summary>
    public static EffectivePermissions DenyAll { get; } =
        new(false, new Dictionary<string, (bool, bool, bool)>(StringComparer.OrdinalIgnoreCase));

    public bool IsSuperAdmin { get; } = isSuperAdmin;

    public bool CanRead(string collection) => Check(collection, g => g.Read, g => g.Read);
    public bool CanWrite(string collection) => Check(collection, g => g.Write, g => g.Write);
    public bool CanDelete(string collection) => Check(collection, g => g.Delete, g => g.Write);

    private bool Check(
        string collection, Func<(bool Read, bool Write, bool Delete), bool> pick,
        Func<(bool Read, bool Write, bool Delete), bool> pickGoverning)
    {
        if (IsSuperAdmin) return true;
        var governing = RbacCollections.GoverningCollection(collection);
        var (name, selector) = governing is null ? (collection, pick) : (governing, pickGoverning);
        return byCollection.TryGetValue(name, out var g) && selector(g);
    }
}
