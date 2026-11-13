using System.Reflection;
using SqlSugar;
using Struo.Domain.Metadata;
using Struo.Domain.Metadata.Attributes;
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

    internal static void Require(PropertyInfo property, CmsFieldAttribute field)
    {
        var sugar = property.GetCustomAttribute<SugarColumn>();
        if (sugar is { IsIgnore: true }) return;

        var name = $"{property.DeclaringType?.Name}.{property.Name}";
        RequireConsistentType(property, field, sugar, name);
        RequireNullabilityConsistent(property, name);
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

        if (shape is not null && sugar is { IsJson: true })
            throw Reject($"Field '{name}' has [ColumnShape] and [SugarColumn(IsJson = true)], which stores it as a JSON column.", "Remove [ColumnShape]", JsonOwner);

        if (hasType && JsonStorageOf(property, sugar) is { } stored)
            throw Reject($"Field '{name}' has {typeText} and {stored}.", "Remove ColumnDataType", JsonOwner);
    }

    private static MetadataException Reject(string declarations, string fix, string why) =>
        new($"{declarations} {fix}: {why}.");

    private static string? JsonStorageOf(PropertyInfo property, SugarColumn? sugar)
    {
        if (sugar is { IsJson: true }) return "[SugarColumn(IsJson = true)], which stores it as a JSON column";
        return ListProperty.Is(property.PropertyType) ? "a List<> type, which is stored as a JSON column" : null;
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
