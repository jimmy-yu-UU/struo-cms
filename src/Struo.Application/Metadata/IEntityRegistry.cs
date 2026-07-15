using System.Reflection;

namespace Struo.Application.Metadata;

public sealed record EntityDescriptor(
    Type EntityType,
    IReadOnlyDictionary<string, string> FieldToProperty,  // camelCase field name -> CLR property name
    string IdProperty)
{
    /// <summary>
    /// CLR property name -> <see cref="PropertyInfo"/> for every public instance property of
    /// <see cref="EntityType"/>, keyed <see cref="StringComparer.OrdinalIgnoreCase"/> and built
    /// once at construction. Lets projection/snapshot hot paths resolve a property accessor via a
    /// cached dictionary lookup instead of per-row <c>Type.GetProperty</c> reflection (CS-4/ARC-2).
    /// Derived from <see cref="EntityType"/> in the record body so every construction site (scanner,
    /// translation descriptor, test fixtures) gets a consistent map with no extra argument.
    /// </summary>
    public IReadOnlyDictionary<string, PropertyInfo> Properties { get; } = BuildProperties(EntityType);

    private static IReadOnlyDictionary<string, PropertyInfo> BuildProperties(Type entityType)
    {
        // First-wins on name collisions (e.g. a `new`-shadowed property) mirrors the enumeration
        // order Type.GetProperties returned; the entities here never shadow, so this matches the
        // single property the old GetProperty(name) resolved.
        var map = new Dictionary<string, PropertyInfo>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in entityType.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            map.TryAdd(p.Name, p);
        return map;
    }
}

public interface IEntityRegistry
{
    EntityDescriptor? Get(string collection);  // case-insensitive
}
