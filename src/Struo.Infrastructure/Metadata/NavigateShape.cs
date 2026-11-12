using System.Reflection;
using SqlSugar;
using Struo.Domain.Metadata;

namespace Struo.Infrastructure.Metadata;

/// <summary>
/// What a property's <c>[Navigate]</c> declares, in the two forms the framework supports:
/// <c>(NavigateType, foreignKey)</c> for many-to-one and one-to-many, and
/// <c>(junctionType, parentKey, targetKey)</c> for many-to-many. The downstream code assumes the
/// foreign key points at the target's primary key, so no other <c>Navigate</c> constructor is read:
/// recognition is by the constructor's parameter list (an omitted <c>params string[]</c> still
/// appears as one null argument), and any other constructor is a <see cref="MetadataException"/>.
/// </summary>
internal readonly record struct NavigateShape(Type? JunctionType, string KeyA, string? KeyB)
{
    private static readonly Type[] ForeignKeyParameters = [typeof(NavigateType), typeof(string), typeof(string[])];
    private static readonly Type[] JunctionParameters = [typeof(Type), typeof(string), typeof(string), typeof(string[])];

    public const string SupportedForms =
        "[Navigate(NavigateType, foreignKey)] for many-to-one and one-to-many, and " +
        "[Navigate(junctionType, parentKey, targetKey)] for many-to-many";

    public bool IsJunction => JunctionType is not null;

    /// <summary>The foreign-key property of a many-to-one or one-to-many relation.</summary>
    public string ForeignKey => KeyA;

    /// <summary>The junction's parent-side key property; only meaningful when <see cref="IsJunction"/>.</summary>
    public string ParentKey => KeyA;

    /// <summary>The junction's target-side key property; only meaningful when <see cref="IsJunction"/>.</summary>
    public string TargetKey => KeyB ?? string.Empty;

    /// <summary>The property's shape, or null when it carries no <c>[Navigate]</c>.</summary>
    public static NavigateShape? Of(PropertyInfo property)
    {
        var data = property.CustomAttributes.FirstOrDefault(a => a.AttributeType == typeof(Navigate));
        return data is null ? null : Read(property, data);
    }

    private static NavigateShape Read(PropertyInfo property, CustomAttributeData data)
    {
        var parameters = data.Constructor.GetParameters().Select(p => p.ParameterType).ToArray();
        var args = data.ConstructorArguments;

        if (parameters.SequenceEqual(ForeignKeyParameters))
            return new NavigateShape(null, args[1].Value as string ?? string.Empty, null);
        if (parameters.SequenceEqual(JunctionParameters))
            return new NavigateShape(
                (Type?)args[0].Value, args[1].Value as string ?? string.Empty, args[2].Value as string ?? string.Empty);

        throw new MetadataException(
            $"Relation '{property.DeclaringType?.Name}.{property.Name}' uses an unsupported [Navigate] constructor " +
            $"({string.Join(", ", parameters.Select(p => p.Name))}). Supported: {SupportedForms}.");
    }
}
