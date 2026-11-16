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
        RequireTypeFitsInterface(property, field, name);
        RequireConsistentType(property, field, sugar, name);
        RequireNullabilityConsistent(property, name);
    }

    // A structured value lives in a JSON column that only the multi-value interfaces and Hidden read and
    // write; the Json interface holds raw JSON text in a string; no interface reads back an object, alone or
    // inside the property type.
    private static void RequireTypeFitsInterface(PropertyInfo property, CmsFieldAttribute field, string name)
    {
        var type = property.PropertyType;
        var held = $"Field '{name}' is a {TypeName(type)} property with Interface = {field.Interface}";

        if (field.Interface == FieldInterface.Json)
        {
            if (type != typeof(string))
                throw Reject($"{held}, which holds raw JSON text.", "Declare the property as string", "the field stores the JSON text itself");
            return;
        }

        if (type == typeof(object))
            throw Reject(
                $"Field '{name}' is an object property with Interface = {field.Interface}.",
                "Declare the concrete type of the value",
                ObjectWhy);

        var nested = ObjectInType.Find(type);
        if (nested is not null)
            throw Reject(
                $"{held}, which contains an object ({nested}).", "Declare the concrete type of the value", ObjectWhy);

        if (field.Interface == FieldInterface.Hidden || FieldInterfaceSets.JsonColumnInterfaces.Contains(field.Interface)) return;
        if (!StructuredType.Is(type)) return;

        var allowed = string.Join(", ", FieldInterfaceSets.JsonColumnInterfaces.Order()) + " or " + FieldInterface.Hidden;
        throw Reject(
            $"{held}, which holds a single value.",
            $"Change the interface to one of {allowed}, or declare a single-value property",
            "a structured value is stored as a JSON column that only those interfaces read and write");
    }

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
    }

    internal static string TypeName(Type type)
    {
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
    };

    private static MetadataException Reject(string declarations, string fix, string why) =>
        new($"{declarations} {fix}: {why}.");

    private static string? JsonStorageOf(PropertyInfo property, SugarColumn? sugar)
    {
        if (sugar is { IsJson: true }) return "[SugarColumn(IsJson = true)], which stores it as a JSON column";
        return StructuredType.Is(property.PropertyType) ? $"a {TypeName(property.PropertyType)} property, which is stored as a JSON column" : null;
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
