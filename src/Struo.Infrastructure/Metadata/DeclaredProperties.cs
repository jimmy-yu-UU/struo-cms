using System.Reflection;

namespace Struo.Infrastructure.Metadata;

/// <summary>
/// Finds the properties of a class hierarchy that carry an attribute the scan reads only from public
/// instance properties. The scan lists <c>BindingFlags.Public | BindingFlags.Instance</c>; this reads the
/// declared properties through <see cref="TypeInfo.DeclaredProperties"/> so it sees the ones the scan skips.
/// </summary>
internal static class DeclaredProperties
{
    /// <summary>
    /// A property declared on <paramref name="type"/> or a base class with <paramref name="attribute"/> on its
    /// own declaration that is static or has no public accessor, or null when there is none.
    /// </summary>
    public static PropertyInfo? FirstMisplaced(Type type, Type attribute) =>
        Hierarchy(type)
            .SelectMany(t => t.GetTypeInfo().DeclaredProperties)
            .FirstOrDefault(prop => prop.IsDefined(attribute, inherit: false) && !IsPublicInstance(prop));

    /// <summary>Why the scan skips <paramref name="prop"/>: <c>static</c> or <c>not public</c>.</summary>
    public static string WhyNotRead(PropertyInfo prop) => AccessorsOf(prop)[0].IsStatic ? "static" : "not public";

    private static IEnumerable<Type> Hierarchy(Type type)
    {
        for (var current = type; current is not null; current = current.BaseType)
            yield return current;
    }

    private static bool IsPublicInstance(PropertyInfo prop)
    {
        var accessors = AccessorsOf(prop);
        return Array.Exists(accessors, accessor => accessor.IsPublic) && !accessors[0].IsStatic;
    }

    // The getter and the setter a property declares, whatever their accessibility; it has at least one.
    private static MethodInfo[] AccessorsOf(PropertyInfo prop) =>
        new[] { prop.GetMethod, prop.SetMethod }.OfType<MethodInfo>().ToArray();
}
