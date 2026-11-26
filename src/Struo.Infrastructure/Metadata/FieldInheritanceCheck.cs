using System.Reflection;
using Struo.Domain.Metadata;
using Struo.Domain.Metadata.Attributes;

namespace Struo.Infrastructure.Metadata;

/// <summary>
/// The scan-time check that a <c>[CmsField]</c> a base class declares is still served in the scanned type.
/// The attribute is not inherited, so a member that overrides or hides the property without carrying
/// <c>[CmsField]</c> itself replaces it, and the field is not served.
/// Why: docs/ai/decisions/cmsfield-is-a-public-instance-property.md
/// </summary>
internal static class FieldInheritanceCheck
{
    private const BindingFlags PublicInstance = BindingFlags.Public | BindingFlags.Instance;

    /// <summary>
    /// Refuses a <c>[CmsField]</c> property of <paramref name="type"/>'s hierarchy that the scan does not
    /// read and whose name no scanned <c>[CmsField]</c> property carries.
    /// </summary>
    public static void Require(Type type)
    {
        var served = type.GetProperties(PublicInstance);
        foreach (var prop in DeclaredProperties.Hierarchy(type).SelectMany(DeclaredProperties.DeclaredOn))
        {
            if (!HasField(prop) || IsServed(prop, served)) continue;
            throw Refusal(prop, Replacement(type, prop));
        }
    }

    private static bool HasField(PropertyInfo prop) => prop.IsDefined(typeof(CmsFieldAttribute), inherit: false);

    // Served: the scan reads this very declaration, or another that carries [CmsField] under the same name.
    private static bool IsServed(PropertyInfo prop, PropertyInfo[] served) =>
        Array.Exists(served, s => s.DeclaringType == prop.DeclaringType && s.Name == prop.Name)
        || Array.Exists(served, s => s.Name == prop.Name && HasField(s));

    // The nearest declaration in a more derived class that replaces the property: an override or a hiding
    // member. A property the scan does not read is replaced by one, or the declaration check refused it.
    private static PropertyInfo Replacement(Type type, PropertyInfo prop) =>
        DeclaredProperties.Hierarchy(type)
            .TakeWhile(t => t != prop.DeclaringType)
            .SelectMany(DeclaredProperties.DeclaredOn)
            .First(p => p.Name == prop.Name);

    private static MetadataException Refusal(PropertyInfo prop, PropertyInfo replacement)
    {
        var declared = $"{prop.DeclaringType!.Name}.{prop.Name}";
        var replacing = $"{replacement.DeclaringType!.Name}.{replacement.Name}";
        return new MetadataException(DeclaredProperties.SameSlot(replacement, prop)
            ? $"Field '{replacing}' overrides '{declared}', which carries [CmsField], without repeating it. " +
              "[CmsField] is not inherited, so the field is not served. " +
              $"Repeat [CmsField(...)] on '{replacing}', or remove it from '{declared}'."
            : $"Field '{declared}' is hidden by '{replacing}', which does not carry [CmsField], so the field is not served. " +
              $"Put [CmsField(...)] on '{replacing}' and declare it public, or remove the hiding member.");
    }
}
