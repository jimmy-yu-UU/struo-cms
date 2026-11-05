namespace Struo.Application.Security;

/// <summary>
/// The caller's resolved permissions for the current request. Super-admin short-circuits every
/// check; otherwise a per-collection grant is consulted and an absent collection is denied. System
/// capabilities (<see cref="HasCapability"/>) follow the same rule.
/// </summary>
public sealed class EffectivePermissions(
    bool isSuperAdmin,
    IReadOnlyDictionary<string, (bool Read, bool Write, bool Delete)> byCollection,
    IReadOnlySet<string>? capabilities = null)
{
    /// <summary>Empty snapshot: not super-admin, no grants (denies everything). The safe default.</summary>
    public static EffectivePermissions DenyAll { get; } =
        new(false, new Dictionary<string, (bool, bool, bool)>(StringComparer.OrdinalIgnoreCase));

    public bool IsSuperAdmin { get; } = isSuperAdmin;

    /// <summary>The capability codes held through roles. Empty for a super-admin, who holds every
    /// capability without any row; ask <see cref="HasCapability"/> rather than reading this.</summary>
    public IReadOnlySet<string> Capabilities { get; } = capabilities ?? new HashSet<string>(StringComparer.Ordinal);

    public bool HasCapability(string code) => IsSuperAdmin || Capabilities.Contains(code);

    public bool CanRead(string collection) => Check(collection, g => g.Read);
    public bool CanWrite(string collection) => Check(collection, g => g.Write);
    public bool CanDelete(string collection) => Check(collection, g => g.Delete);

    private bool Check(string collection, Func<(bool Read, bool Write, bool Delete), bool> pick) =>
        IsSuperAdmin || (byCollection.TryGetValue(collection, out var g) && pick(g));
}
