using System.Reflection;
using Struo.Domain.Metadata;

namespace Struo.Infrastructure.Metadata;

/// <summary>
/// The scan-time checks of a many-to-many <c>SortField</c>: it names a junction property that has a column,
/// is no key of the junction, has a type the link writer orders by, and has a setter. The link writer
/// stores each link's position (0, 1, 2, ...) in that property.
/// </summary>
internal static class SortFieldCheck
{
    private const BindingFlags DeclaredAny =
        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly;

    private static readonly Type[] OrderTypes =
    [
        typeof(short), typeof(ushort), typeof(int), typeof(uint), typeof(long), typeof(ulong),
        typeof(float), typeof(double), typeof(decimal), typeof(string),
    ];

    private static readonly string OrderTypeNames = ListOf(OrderTypes.Select(ColumnDeclarationCheck.TypeName));

    public static void Require(PropertyInfo relation, NavigateShape nav, string sortField)
    {
        if (!nav.IsJunction)
            throw new MetadataException(
                $"Relation '{RelationDeclarationCheck.RelationName(relation)}' declares SortField '{sortField}', " +
                "but SortField orders the links of a many-to-many relation and this relation is not one. " +
                "Remove SortField from it.");

        var junction = nav.JunctionType!;
        if (string.IsNullOrWhiteSpace(sortField))
            throw new MetadataException(
                $"Relation '{RelationDeclarationCheck.RelationName(relation)}' declares a blank SortField in " +
                "[CmsRelation], which names no junction property, so the links keep no order. " +
                $"Name a property of {junction.Name} that holds the position, or remove SortField from it.");

        var property = RelationDeclarationCheck.RequireColumn(
            relation, junction, $"SortField '{sortField}' in [CmsRelation]", sortField, "sort field",
            "the links cannot be ordered by it");
        RequireNotAKey(relation, nav, property);
        RequireOrderType(relation, property);
        RequireSetter(relation, property, junction);
    }

    private static void RequireNotAKey(PropertyInfo relation, NavigateShape nav, PropertyInfo property)
    {
        if (RoleOf(nav, property) is not { } role) return;

        throw Refuse(
            relation, property,
            $"is {role}: the link writer stores the position in the SortField property, so it would overwrite " +
            "the key. Give the junction a property of its own for the order and name that as SortField.");
    }

    private static string? RoleOf(NavigateShape nav, PropertyInfo property)
    {
        if (property.Name == nav.ParentKey) return "the junction parent key";
        if (property.Name == nav.TargetKey) return "the junction target key";
        return KeyLookup.PrimaryKeyProperties(nav.JunctionType!).Exists(key => key.Name == property.Name)
            ? "the primary key"
            : null;
    }

    private static void RequireOrderType(PropertyInfo relation, PropertyInfo property)
    {
        var type = property.PropertyType;
        if (OrderTypes.Contains(Nullable.GetUnderlyingType(type) ?? type)) return;

        throw Refuse(
            relation, property,
            $"is of type {ColumnDeclarationCheck.TypeName(type)}: the link writer stores the position " +
            "(0, 1, 2, ...) in it, and the links cannot be ordered by a property of that type. Declare the sort " +
            $"property as {OrderTypeNames} (a nullable value type is accepted too).");
    }

    private static void RequireSetter(PropertyInfo relation, PropertyInfo property, Type junction)
    {
        if (property.SetMethod is not null) return;

        var owner = property.DeclaringType!;
        if (owner.GetProperty(property.Name, DeclaredAny)?.GetSetMethod(nonPublic: true) is not null)
            throw Refuse(
                relation, property,
                $"has a private setter on the base class {owner.Name}, which {junction.Name} cannot reach: the " +
                $"link writer assigns the position to it. Make the setter protected, init or public on {owner.Name}, " +
                $"or declare the sort property on {junction.Name}.");

        throw Refuse(
            relation, property,
            "has no setter: the link writer assigns the position to it. Give it a set or init accessor.");
    }

    private static MetadataException Refuse(PropertyInfo relation, PropertyInfo property, string detail) =>
        new($"Relation '{RelationDeclarationCheck.RelationName(relation)}' declares " +
            $"SortField '{property.Name}' in [CmsRelation], but {property.DeclaringType!.Name}.{property.Name} " +
            detail);

    private static string ListOf(IEnumerable<string> names)
    {
        var all = names.ToList();
        return $"{string.Join(", ", all.Take(all.Count - 1))} or {all[^1]}";
    }
}
