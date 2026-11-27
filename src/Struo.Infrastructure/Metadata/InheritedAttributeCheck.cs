using System.Reflection;
using Struo.Domain.Metadata;

namespace Struo.Infrastructure.Metadata;

/// <summary>
/// The scan-time check that a declaration carrying an attribute the scan does not inherit is still read in
/// the scanned type. A member that overrides or hides such a property without carrying the attribute itself
/// replaces it, and the scan reads nothing from the replacement.
/// </summary>
internal static class InheritedAttributeCheck
{
    private const BindingFlags PublicInstance = BindingFlags.Public | BindingFlags.Instance;

    /// <summary>
    /// Throws what <paramref name="refusal"/> returns, given the property and its replacement, for the first
    /// property of <paramref name="type"/>'s hierarchy that carries <paramref name="attribute"/> on its own
    /// declaration, is not read by the scan and has no same-named property that carries it.
    /// </summary>
    public static void Require(
        Type type, Type attribute, Func<PropertyInfo, PropertyInfo, MetadataException> refusal)
    {
        var served = type.GetProperties(PublicInstance);
        foreach (var prop in DeclaredProperties.Hierarchy(type).SelectMany(DeclaredProperties.DeclaredOn))
        {
            if (!HasAttribute(prop, attribute) || IsServed(prop, served, attribute)) continue;
            throw refusal(prop, Replacement(type, prop));
        }
    }

    private static bool HasAttribute(PropertyInfo prop, Type attribute) => prop.IsDefined(attribute, inherit: false);

    // Served: the scan reads this very declaration, or another that carries the attribute under the same name.
    private static bool IsServed(PropertyInfo prop, PropertyInfo[] served, Type attribute) =>
        Array.Exists(served, s => s.DeclaringType == prop.DeclaringType && s.Name == prop.Name)
        || Array.Exists(served, s => s.Name == prop.Name && HasAttribute(s, attribute));

    // The nearest declaration in a more derived class that replaces the property: an override or a hiding
    // member. A property the scan does not read is replaced by one, or the declaration check refused it.
    private static PropertyInfo Replacement(Type type, PropertyInfo prop) =>
        DeclaredProperties.Hierarchy(type)
            .TakeWhile(t => t != prop.DeclaringType)
            .SelectMany(DeclaredProperties.DeclaredOn)
            .First(p => p.Name == prop.Name);
}
