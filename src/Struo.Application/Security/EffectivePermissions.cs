namespace Struo.Application.Security;

/// <summary>
/// The caller's resolved permissions for the current request. Super-admin short-circuits every
/// check; otherwise a per-collection grant is consulted and an absent collection is denied.
/// </summary>
public sealed class EffectivePermissions(
    bool isSuperAdmin,
    IReadOnlyDictionary<string, (bool Read, bool Write, bool Delete)> byCollection)
{
    /// <summary>Empty snapshot: not super-admin, no grants (denies everything). The safe default.</summary>
    public static EffectivePermissions DenyAll { get; } =
        new(false, new Dictionary<string, (bool, bool, bool)>(StringComparer.OrdinalIgnoreCase));

    public bool IsSuperAdmin { get; } = isSuperAdmin;

    public bool CanRead(string collection) => Check(collection, g => g.Read);
    public bool CanWrite(string collection) => Check(collection, g => g.Write);
    public bool CanDelete(string collection) => Check(collection, g => g.Delete);

    private bool Check(string collection, Func<(bool Read, bool Write, bool Delete), bool> pick) =>
        IsSuperAdmin || (byCollection.TryGetValue(collection, out var g) && pick(g));
}
