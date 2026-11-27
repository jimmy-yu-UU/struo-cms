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
    /// <summary>
    /// Refuses a <c>[CmsField]</c> property of <paramref name="type"/>'s hierarchy that the scan does not
    /// read and whose name no scanned <c>[CmsField]</c> property carries.
    /// </summary>
    public static void Require(Type type) =>
        InheritedAttributeCheck.Require(type, typeof(CmsFieldAttribute), Refusal);

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
