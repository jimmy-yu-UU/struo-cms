using System.Reflection;
using Struo.Domain.Metadata;
using Struo.Domain.Metadata.Attributes;

namespace Struo.Infrastructure.Metadata;

/// <summary>
/// The scan-time check that a <c>[CmsRelation]</c> a base class declares is still served in the scanned
/// type. The attribute is not inherited, so a member that overrides or hides the property without carrying
/// <c>[CmsRelation]</c> itself replaces it, and the relation is not served, whatever the replacement does
/// with <c>[Navigate]</c>: the schema does not list it, a read does not expand it and a write does not link
/// it. A replacement that repeats <c>[CmsRelation]</c> without <c>[Navigate]</c> is refused by the scan of
/// the relation itself. The collection scan runs this check before the column override check, so a bare
/// override gets this refusal, which names every attribute to repeat. Interface members are not part of the
/// hierarchy and are not looked at.
/// Why: docs/ai/decisions/relation-key-and-primary-key-declarations.md
/// </summary>
internal static class RelationInheritanceCheck
{
    private const string NotServed =
        "so the relation is not served: the schema does not list it, a read does not expand it and a write does not link it.";

    /// <summary>
    /// Refuses a <c>[CmsRelation]</c> property of <paramref name="type"/>'s hierarchy that the scan does not
    /// read and whose name no scanned <c>[CmsRelation]</c> property carries.
    /// </summary>
    public static void Require(Type type) =>
        InheritedAttributeCheck.Require(type, typeof(CmsRelationAttribute), Refusal);

    private static MetadataException Refusal(PropertyInfo prop, PropertyInfo replacement)
    {
        var declared = $"{prop.DeclaringType!.Name}.{prop.Name}";
        var replacing = $"{replacement.DeclaringType!.Name}.{replacement.Name}";
        return new MetadataException(DeclaredProperties.SameSlot(replacement, prop)
            ? $"Relation '{replacing}' overrides '{declared}', which carries [CmsRelation], without repeating it. " +
              $"[CmsRelation] is not inherited, {NotServed} " +
              $"Repeat [CmsRelation(...)], [Navigate(...)] and [SugarColumn(IsIgnore = true)] on '{replacing}', " +
              $"or remove [CmsRelation] and [Navigate] from '{declared}'."
            : $"Relation '{declared}' is hidden by '{replacing}', which does not carry [CmsRelation], {NotServed} " +
              $"Put [CmsRelation(...)], [Navigate(...)] and [SugarColumn(IsIgnore = true)] on '{replacing}' and " +
              "declare it public, or remove the hiding member.");
    }
}
