using System.Reflection;
using Struo.Domain.Auditing;
using Struo.Domain.Metadata;
using Struo.Domain.Metadata.Attributes;

namespace Struo.Infrastructure.Metadata;

/// <summary>
/// The scan-time check that a soft-deletable collection declares no <c>[CmsField]</c> on <c>DeletedAt</c> or
/// <c>DeletedBy</c>. The delete and restore operations own those columns and a body never sets them, so a field
/// there could only ever be read, while the schema and the admin form would offer it as editable.
/// Why: docs/ai/decisions/cmsfield-is-a-public-instance-property.md
/// </summary>
internal static class SoftDeleteFieldCheck
{
    private static readonly string[] Columns = [nameof(ISoftDeletable.DeletedAt), nameof(ISoftDeletable.DeletedBy)];

    /// <summary>
    /// A <c>[CmsField]</c> on <c>DeletedAt</c> or <c>DeletedBy</c> of <paramref name="type"/>, when it is
    /// soft-deletable, is refused. A class that is not soft-deletable keeps such a property as an ordinary field.
    /// </summary>
    public static void Require(Type type)
    {
        if (!typeof(ISoftDeletable).IsAssignableFrom(type)) return;
        var declared = type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .FirstOrDefault(p => Columns.Contains(p.Name) && p.GetCustomAttribute<CmsFieldAttribute>() is not null);
        if (declared is null) return;

        throw new MetadataException(
            $"Field '{type.Name}.{declared.Name}' has [CmsField], but {type.Name} is soft-deletable and the delete and " +
            $"restore operations own '{declared.Name}': a request body never sets it, so the field could only be read, " +
            "while the schema and the admin form would offer it as editable. Remove [CmsField]; the item projection " +
            "does not return soft-delete state.");
    }
}
