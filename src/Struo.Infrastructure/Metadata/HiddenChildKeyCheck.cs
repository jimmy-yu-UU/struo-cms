using System.Reflection;
using SqlSugar;
using Struo.Domain.Metadata;
using Struo.Domain.Metadata.Attributes;

namespace Struo.Infrastructure.Metadata;

/// <summary>
/// The scan-time check that the child key of a one-to-many relation can be named by a reader. The admin lists the
/// children by filtering the child collection on that key, and a key whose field sets <c>Hidden = true</c> is
/// refused by name like any hidden field. The list instead filters through a many-to-one relation of the child
/// that points back at the parent on the same key, so a hidden key is accepted only when the child declares one.
/// Why: docs/ai/decisions/relation-key-and-primary-key-declarations.md
/// </summary>
internal static class HiddenChildKeyCheck
{
    private const BindingFlags PublicInstance = BindingFlags.Public | BindingFlags.Instance;

    /// <summary>
    /// Refuses the one-to-many relation <paramref name="relation"/> when its child key <paramref name="key"/> of
    /// <paramref name="child"/> sets <c>Hidden = true</c> and <paramref name="child"/> has no many-to-one
    /// relation on that key that points at the collection of <paramref name="parent"/>.
    /// </summary>
    public static void Require(
        PropertyInfo relation, PropertyInfo key, Type child, Type parent, IReadOnlyDictionary<string, Type> collections)
    {
        if (key.GetCustomAttribute<CmsFieldAttribute>() is not { Hidden: true }) return;
        if (HasRelationBack(key, child, parent, collections)) return;

        var parentName = parent.Name;
        var childKey = $"{child.Name}.{key.Name}";
        throw new MetadataException(
            $"Relation '{RelationDeclarationCheck.RelationName(relation)}' lists the rows of '{child.Name}' by their " +
            $"key '{childKey}', but that key sets Hidden = true, so a reader cannot filter '{child.Name}' by it: the " +
            "admin list of the relation answers 400 Unknown field. " +
            $"Declare a many-to-one [CmsRelation] on '{childKey}' that points at '{parentName}', which the list " +
            $"filters through, or remove Hidden = true from '{childKey}'.");
    }

    private static bool HasRelationBack(
        PropertyInfo key, Type child, Type parent, IReadOnlyDictionary<string, Type> collections)
    {
        var parentCollection = MetadataScanner.CollectionNameOf(parent);
        return child.GetProperties(PublicInstance)
            .Where(p => p.GetCustomAttribute<CmsRelationAttribute>() is not null && !NavigateShape.IsListProperty(p))
            .Any(p => NavigateShape.Of(p) is { RelationType: NavigateType.ManyToOne } nav
                      && string.Equals(nav.ForeignKey, key.Name, StringComparison.Ordinal)
                      && string.Equals(
                          MetadataScanner.CollectionNameOf(RelationDeclarationCheck.CollectionClassOf(p.PropertyType, collections) ?? p.PropertyType),
                          parentCollection, StringComparison.OrdinalIgnoreCase));
    }
}
