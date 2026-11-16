using System.Reflection;

namespace Struo.Infrastructure.Metadata;

/// <summary>
/// Finds an <see cref="object"/> inside a property type: as a generic argument, as an array element
/// or as the type of a public instance property of a class or struct, to any depth. A value of such a
/// type comes back from a JSON column as a JSON node, not as the value that was written.
/// </summary>
internal static class ObjectInType
{
    /// <summary>
    /// Describes where the first <see cref="object"/> sits inside <paramref name="type"/>, or returns
    /// null when there is none. <paramref name="type"/> itself being <see cref="object"/> is not
    /// reported here. Members are walked only for types outside the <c>System</c> and
    /// <c>Microsoft</c> namespaces, and each type is visited once, so a type that refers to itself ends.
    /// </summary>
    internal static string? Find(Type type) => Find(type, []);

    private static string? Find(Type type, HashSet<Type> visited)
    {
        type = Nullable.GetUnderlyingType(type) ?? type;
        if (!visited.Add(type)) return null;

        if (type.IsArray)
        {
            var element = type.GetElementType()!;
            return element == typeof(object) ? $"element type of {Name(type)}" : Find(element, visited);
        }

        if (type.IsGenericType)
        {
            foreach (var argument in type.GetGenericArguments())
            {
                if (argument == typeof(object)) return $"type argument of {Name(type)}";
                var inner = Find(argument, visited);
                if (inner is not null) return inner;
            }
        }

        return IsWalkable(type) ? FindInMembers(type, visited) : null;
    }

    private static string? FindInMembers(Type type, HashSet<Type> visited)
    {
        foreach (var member in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (!member.CanRead || member.GetIndexParameters().Length > 0) continue;
            if (member.PropertyType == typeof(object)) return $"member {type.Name}.{member.Name}";
            var inner = Find(member.PropertyType, visited);
            if (inner is not null) return inner;
        }
        return null;
    }

    private static bool IsWalkable(Type type) =>
        (type.IsClass || type.IsValueType) && !type.IsPrimitive && !type.IsEnum && !type.IsPointer &&
        type.Namespace is not null &&
        !type.Namespace.StartsWith("System", StringComparison.Ordinal) &&
        !type.Namespace.StartsWith("Microsoft", StringComparison.Ordinal);

    private static string Name(Type type) => ColumnDeclarationCheck.TypeName(type);
}
