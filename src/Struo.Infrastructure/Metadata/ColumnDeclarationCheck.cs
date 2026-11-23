using System.Reflection;
using SqlSugar;
using Struo.Domain.Metadata;
using Struo.Domain.Metadata.Attributes;
using Struo.Domain.Metadata.Enums;
using Struo.Infrastructure.Persistence;

namespace Struo.Infrastructure.Metadata;

/// <summary>
/// Rejects a <c>[CmsField]</c> property whose column declarations contradict each other, so the
/// framework never has to ignore one of them. <c>MetadataScanner</c> runs it for every property that
/// has a column.
/// </summary>
internal static class ColumnDeclarationCheck
{
    private const string JsonOwner = "the framework chooses the type of a JSON column";
    private const string JsonStorage = "which is always stored as a JSON column";
    private const string ObjectWhy = "a value read from an object comes back as a JSON node, not as the value that was written";

    internal static void Require(PropertyInfo property, CmsFieldAttribute field)
    {
        var sugar = property.GetCustomAttribute<SugarColumn>();
        if (sugar is { IsIgnore: true }) return;

        var name = $"{property.DeclaringType?.Name}.{property.Name}";
        RequireTypeFitsInterface(property, field, $"Field '{name}'");
        RequireConsistentType(property, field, sugar, name);
        RequireNullabilityConsistent(property, name);
    }

    /// <summary>
    /// The type checks of <see cref="Require"/> for a Repeater sub-field, which has no column and so no
    /// column declarations to check.
    /// </summary>
    internal static void RequireSubFieldType(PropertyInfo property, CmsFieldAttribute field, string repeater) =>
        RequireTypeFitsInterface(
            property, field, $"Sub-field '{property.DeclaringType?.Name}.{property.Name}' of Repeater '{repeater}'");

    // A structured value lives in a JSON column that only the multi-value interfaces and Hidden read and
    // write; the Json interface holds raw JSON text in a string; no interface reads back an object, alone or
    // inside the property type. A bare object is reported first (its type is the problem), then a structured
    // type on a single-value interface (the outer problem), then an object inside the type.
    private static void RequireTypeFitsInterface(PropertyInfo property, CmsFieldAttribute field, string subject)
    {
        var type = property.PropertyType;
        var held = $"{subject} is {WithArticle(TypeName(type))} property with Interface = {field.Interface}";

        if (field.Interface == FieldInterface.Json)
        {
            if (type != typeof(string))
                throw Reject($"{held}, which holds raw JSON text.", "Declare the property as string", "the field stores the JSON text itself");
            return;
        }

        if (type == typeof(object))
            throw Reject(
                $"{subject} is an object property with Interface = {field.Interface}.",
                "Declare the concrete type of the value",
                ObjectWhy);

        var holdsStructured = field.Interface != FieldInterface.Hidden &&
                              !FieldInterfaceSets.JsonColumnInterfaces.Contains(field.Interface) &&
                              StructuredType.Is(type);
        if (holdsStructured) throw RejectStructured(held);

        var nested = ObjectInType.Find(type);
        if (nested is not null)
            throw Reject(
                $"{held}, which contains an object ({nested}).", "Declare the concrete type of the value", ObjectWhy);

        // A Repeater's list type is checked, with its own message, where the child fields are read.
        if (field.Interface != FieldInterface.Repeater && !InterfacePropertyTypes.Accepts(field.Interface, type))
            throw RejectTypeMismatch(held, field.Interface, type);
    }

    private const string TypeWhy = "a field value is read and written as the property type its interface expects";

    private static MetadataException RejectTypeMismatch(string held, FieldInterface member, Type type)
    {
        var accepting = InterfacePropertyTypes.Accepting(type);
        if (accepting.Count == 0)
            return new MetadataException(
                $"{held}, and no field interface accepts {InterfacePropertyTypes.Display(type)}; {Suggest(type)}: {TypeWhy}.");

        // Hidden holds a value but shows no editor, and Divider holds none, so neither is offered as a way out.
        var others = accepting.Where(i => i is not (FieldInterface.Hidden or FieldInterface.Divider)).ToList();
        var instead = others.Count == 0 ? "" : $", or change the interface to {Offer(others, type)}";
        return new MetadataException(
            $"{held}, but {member} holds {InterfacePropertyTypes.Describe(member)}; " +
            $"declare the property as {InterfacePropertyTypes.Declare(member)}{instead}: {TypeWhy}.");
    }

    private const int OfferedInterfaces = 4;

    private static string Offer(List<FieldInterface> interfaces, Type type) =>
        interfaces.Count <= OfferedInterfaces
            ? InterfacePropertyTypes.JoinOr(interfaces.Select(i => i.ToString()))
            : string.Join(", ", interfaces.Take(OfferedInterfaces)) +
              $" or another interface that holds {InterfacePropertyTypes.Display(type)} " +
              "(the field types chapter of the manual lists them)";

    // Reached only by a single-value type that no interface holds: an enum, a date or time type other than
    // DateTime, char, or a primitive other than the table's numbers and bool.
    private static string Suggest(Type type)
    {
        var single = Nullable.GetUnderlyingType(type) ?? type;
        if (single.IsEnum)
            return "declare the property as string with Interface = Select or Radio and the values in [CmsOptions]";
        if (single == typeof(DateOnly) || single == typeof(DateTimeOffset))
            return "declare the property as DateTime (Interface = Date, Time or DateTime)";
        if (single == typeof(TimeOnly) || single == typeof(TimeSpan))
            return "declare the property as DateTime (Interface = Time), or as string (Interface = Text) to hold the text";
        if (single == typeof(char)) return "declare the property as string";
        return "declare the property as int or long (Interface = Number)";
    }

    private static MetadataException RejectStructured(string held)
    {
        var allowed = string.Join(", ", FieldInterfaceSets.JsonColumnInterfaces.Order()) + " or " + FieldInterface.Hidden;
        return Reject(
            $"{held}, which holds a single value.",
            $"Change the interface to one of {allowed}, or declare a single-value property",
            "a structured value is stored as a JSON column that only those interfaces read and write");
    }

    // "an" before the sounds of a, e, i and o; "uint" and "ushort" start with the sound of "you".
    internal static string WithArticle(string typeName) =>
        "aeiouAEIOU".Contains(typeName[0], StringComparison.Ordinal) && !typeName.StartsWith('u') && !typeName.StartsWith('U')
            ? $"an {typeName}"
            : $"a {typeName}";

    private static void RequireConsistentType(PropertyInfo property, CmsFieldAttribute field, SugarColumn? sugar, string name)
    {
        var shape = property.GetCustomAttribute<ColumnShapeAttribute>()?.Shape;
        var declaredType = sugar?.ColumnDataType;
        var hasType = !string.IsNullOrEmpty(declaredType);
        var typeText = $"[SugarColumn(ColumnDataType = \"{declaredType}\")]";

        if (FieldInterfaceSets.JsonColumnInterfaces.Contains(field.Interface))
        {
            var reason = $"Interface = {field.Interface}, {JsonStorage}";
            if (shape is not null) throw Reject($"Field '{name}' has [ColumnShape] and {reason}.", "Remove [ColumnShape]", JsonOwner);
            if (hasType) throw Reject($"Field '{name}' has {typeText} and {reason}.", "Remove ColumnDataType", JsonOwner);
            return;
        }

        if (shape is not null && hasType)
            throw new MetadataException(
                $"Field '{name}' has both [ColumnShape({shape})] and {typeText}. " +
                "Remove ColumnDataType to use the shape, or remove [ColumnShape] to use your own type.");

        var stored = JsonStorageOf(property, sugar);
        if (shape is not null && stored is not null)
            throw Reject($"Field '{name}' has [ColumnShape] and {stored}.", "Remove [ColumnShape]", JsonOwner);

        if (hasType && stored is not null)
            throw Reject($"Field '{name}' has {typeText} and {stored}.", "Remove ColumnDataType", JsonOwner);

        RequireShapeFitsType(property, shape, name);
        if (sugar is { IsJson: true } && !StructuredType.Is(property.PropertyType))
            throw Reject(
                $"Field '{name}' is {WithArticle(TypeName(property.PropertyType))} property with [SugarColumn(IsJson = true)], which holds a single value.",
                "Remove IsJson, or declare a structured property (List<T>, Dictionary<K, V> or a class)",
                "IsJson stores the value as JSON, and a single value comes back with an extra layer of quotes");

        if (hasType) RequireColumnTypeFits(property, declaredType!, typeText, name);
    }

    private static void RequireColumnTypeFits(PropertyInfo property, string declaredType, string typeText, string name)
    {
        if (ColumnTypeFit.Find(property.PropertyType, declaredType) is not { } misfit) return;

        throw Reject(
            $"Field '{name}' has {typeText} and is {WithArticle(TypeName(property.PropertyType))} property.",
            misfit.Fix,
            misfit.Why);
    }

    private static void RequireShapeFitsType(PropertyInfo property, ColumnShape? shape, string name)
    {
        var type = property.PropertyType;
        var single = Nullable.GetUnderlyingType(type) ?? type;
        var onWrongType = shape switch
        {
            ColumnShape.LongText => type != typeof(string),
            ColumnShape.Timestamp or ColumnShape.TimestampWithTimeZone => single != typeof(DateTime),
            _ => false,
        };
        if (!onWrongType) return;

        var wants = shape == ColumnShape.LongText ? "string" : "DateTime";
        throw Reject(
            $"Field '{name}' has [ColumnShape({shape})] and is {WithArticle(TypeName(type))} property.",
            $"Remove [ColumnShape], or declare the property as {wants}",
            $"the shape gives the column a {(shape == ColumnShape.LongText ? "text" : "date-time")} type that {WithArticle(InterfacePropertyTypes.Display(type))} cannot be read from");
    }

    internal static string TypeName(Type type)
    {
        if (Nullable.GetUnderlyingType(type) is { } inner) return TypeName(inner) + "?";
        if (type.IsArray) return TypeName(type.GetElementType()!) + "[]";
        if (!type.IsGenericType) return Keywords.GetValueOrDefault(type, type.Name);

        var name = type.Name[..type.Name.IndexOf('`')];
        return $"{name}<{string.Join(", ", type.GetGenericArguments().Select(TypeName))}>";
    }

    private static readonly Dictionary<Type, string> Keywords = new()
    {
        [typeof(string)] = "string", [typeof(int)] = "int", [typeof(long)] = "long", [typeof(bool)] = "bool",
        [typeof(decimal)] = "decimal", [typeof(double)] = "double", [typeof(float)] = "float",
        [typeof(short)] = "short", [typeof(byte)] = "byte", [typeof(char)] = "char", [typeof(object)] = "object",
        [typeof(uint)] = "uint", [typeof(ushort)] = "ushort", [typeof(ulong)] = "ulong", [typeof(sbyte)] = "sbyte",
    };

    private static MetadataException Reject(string declarations, string fix, string why) =>
        new($"{declarations} {fix}: {why}.");

    private static string? JsonStorageOf(PropertyInfo property, SugarColumn? sugar)
    {
        if (sugar is { IsJson: true }) return "[SugarColumn(IsJson = true)], which stores it as a JSON column";
        return StructuredType.Is(property.PropertyType) ? $"{WithArticle(TypeName(property.PropertyType))} property, which is stored as a JSON column" : null;
    }

    private static void RequireNullabilityConsistent(PropertyInfo property, string name)
    {
        if (!DeclaresNotNull(property) || !ClrNullability.GivesNullableColumn(property)) return;

        throw new MetadataException(
            $"Field '{name}' is nullable but declares [SugarColumn(IsNullable = false)], and a nullable property " +
            "always gets a nullable column. Declare the property non-nullable (Guid, not Guid?; string, not string?) " +
            "or remove IsNullable = false.");
    }

    // IsNullable is a plain bool that is false unless set, so only the attribute's written arguments tell
    // an explicit IsNullable = false from a [SugarColumn] that never mentions it.
    private static bool DeclaresNotNull(PropertyInfo property) =>
        property.GetCustomAttributesData()
            .Where(d => d.AttributeType == typeof(SugarColumn))
            .SelectMany(d => d.NamedArguments)
            .Any(a => a.MemberName == nameof(SugarColumn.IsNullable) && a.TypedValue.Value is false);
}
