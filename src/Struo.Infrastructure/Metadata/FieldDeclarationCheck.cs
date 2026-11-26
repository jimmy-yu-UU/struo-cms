using Struo.Domain.Metadata;
using Struo.Domain.Metadata.Attributes;

namespace Struo.Infrastructure.Metadata;

/// <summary>
/// The scan-time check that a <c>[CmsField]</c> property is one the scan reads: a public instance property.
/// Why: docs/ai/decisions/cmsfield-is-a-public-instance-property.md
/// </summary>
internal static class FieldDeclarationCheck
{
    /// <summary>
    /// A <c>[CmsField]</c> property of <paramref name="type"/> or of a base class is a public instance
    /// property: the scan reads no other, so the field would silently not be served.
    /// </summary>
    public static void RequirePublicInstanceFields(Type type)
    {
        if (DeclaredProperties.FirstMisplaced(type, typeof(CmsFieldAttribute)) is not { } misplaced) return;

        throw new MetadataException(
            $"Field '{misplaced.DeclaringType!.Name}.{misplaced.Name}' has [CmsField] but is " +
            $"{DeclaredProperties.WhyNotRead(misplaced)}, so the field is not served. " +
            "A field is a public instance property: declare it public and without static, or remove [CmsField].");
    }
}
