using System.Collections;
using System.Reflection;

namespace Struo.Infrastructure.Metadata;

/// <summary>
/// Finds an <see cref="object"/> inside a property type: as a generic argument, as an array element
/// or as the type of a public instance property of a class or struct, to any depth. A value of such a
/// type comes back from a JSON column as a JSON node, not as the value that was written. A non-generic
/// collection holds untyped elements and is reported the same way.
/// </summary>
internal static class ObjectInType
{
    /// <summary>
    /// Describes where the first <see cref="object"/> sits inside <paramref name="type"/>, or returns
    /// null when there is none. <paramref name="type"/> itself being <see cref="object"/> is not
    /// reported here. Members are walked only for types outside the <c>System</c> and <c>Microsoft</c>
    /// namespaces and their sub-namespaces (a type with no namespace is walked), and each type is visited
    /// once, so a type that refers to itself ends.
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

        if (IsUntypedCollection(type)) return $"untyped elements of {Name(type)}";

        return IsWalkable(type) ? FindInMembers(type, visited) : null;
    }

    private static string? FindInMembers(Type type, HashSet<Type> visited)
    {
        foreach (var member in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (!member.CanRead || member.GetIndexParameters().Length > 0) continue;
            if (member.PropertyType == typeof(object)) return $"member {Name(type)}.{member.Name}";
            var inner = Find(member.PropertyType, visited);
            if (inner is not null) return inner;
        }
        return null;
    }

    private static bool IsWalkable(Type type) =>
        (type.IsClass || type.IsValueType) && !type.IsPrimitive && !type.IsEnum && !type.IsPointer &&
        !InFrameworkNamespace(type.Namespace);

    private static bool InFrameworkNamespace(string? ns) =>
        ns is "System" or "Microsoft" ||
        (ns is not null && (ns.StartsWith("System.", StringComparison.Ordinal) ||
                            ns.StartsWith("Microsoft.", StringComparison.Ordinal)));

    // A type that enumerates without saying what: IEnumerable without IEnumerable<T> (ArrayList, Hashtable, IList).
    private static bool IsUntypedCollection(Type type) =>
        typeof(IEnumerable).IsAssignableFrom(type) &&
        !type.GetInterfaces().Append(type).Any(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IEnumerable<>));

    private static string Name(Type type) => ColumnDeclarationCheck.TypeName(type);
}
