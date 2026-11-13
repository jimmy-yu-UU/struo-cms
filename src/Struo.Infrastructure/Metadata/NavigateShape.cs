using System.Collections;
using System.Reflection;
using SqlSugar;
using Struo.Domain.Metadata;

namespace Struo.Infrastructure.Metadata;

/// <summary>
/// What a property's <c>[Navigate]</c> declares, in the two forms the framework supports:
/// <c>(NavigateType, foreignKey)</c> for many-to-one and one-to-many, and
/// <c>(junctionType, parentKey, targetKey)</c> for many-to-many. The downstream code assumes the
/// foreign key points at the target's primary key, so no other <c>Navigate</c> constructor is read.
/// Recognition is by the constructor's parameter list: the trailing parameter of both forms is
/// SqlSugar's optional <c>string[] queryPropertyNames</c>, which a declaration that omits it leaves as
/// one null argument.
/// </summary>
/// <remarks>
/// <see cref="Of"/> is for a property that carries <c>[CmsRelation]</c>: the CMS reads its navigation, so
/// any unsupported declaration is a <see cref="MetadataException"/>. <see cref="TryOf"/> is for a property
/// the CMS does not read as a relation: it recognises the two forms and returns null for anything else.
/// </remarks>
internal sealed class NavigateShape
{
    private static readonly Type[] ForeignKeyParameters = [typeof(NavigateType), typeof(string), typeof(string[])];
    private static readonly Type[] JunctionParameters = [typeof(Type), typeof(string), typeof(string), typeof(string[])];

    public const string SupportedForms =
        "[Navigate(NavigateType, foreignKey)] for many-to-one and one-to-many, and " +
        "[Navigate(junctionType, parentKey, targetKey)] for many-to-many";

    private NavigateShape()
    {
    }

    /// <summary>True when the declaration used the junction constructor, whatever its arguments.</summary>
    public bool IsJunction { get; private init; }

    /// <summary>The <see cref="NavigateType"/> of the foreign-key form; meaningless for the junction form.</summary>
    public NavigateType RelationType { get; private init; }

    public Type? JunctionType { get; private init; }

    /// <summary>The foreign-key property of a many-to-one or one-to-many relation.</summary>
    public string ForeignKey { get; private init; } = string.Empty;

    /// <summary>The junction's parent-side key property.</summary>
    public string ParentKey { get; private init; } = string.Empty;

    /// <summary>The junction's target-side key property.</summary>
    public string TargetKey { get; private init; } = string.Empty;

    /// <summary>A list property (<c>List&lt;T&gt;</c> and the like) holds many entities; any other holds one.</summary>
    public static bool IsListProperty(PropertyInfo property) =>
        property.PropertyType.IsGenericType && typeof(IEnumerable).IsAssignableFrom(property.PropertyType);

    /// <summary>
    /// The shape of a <c>[CmsRelation]</c> property, or null when it carries no <c>[Navigate]</c>. Anything
    /// the CMS cannot read faithfully throws a <see cref="MetadataException"/>.
    /// </summary>
    public static NavigateShape? Of(PropertyInfo property)
    {
        var declared = property.CustomAttributes.Where(a => typeof(Navigate).IsAssignableFrom(a.AttributeType)).ToList();
        if (declared.Count == 0) return null;

        var derived = declared.FirstOrDefault(a => a.AttributeType != typeof(Navigate));
        if (derived is not null)
            throw Unsupported(
                property, $"{derived.AttributeType.Name} derives from Navigate, and the CMS reads [Navigate] itself");

        var shape = Recognise(declared[0]);
        if (shape is null)
        {
            var parameters = declared[0].Constructor.GetParameters().Select(p => p.Name);
            throw Unsupported(property, $"constructor ({string.Join(", ", parameters)})");
        }

        shape.Validate(property);
        return shape;
    }

    /// <summary>
    /// The shape of a property the CMS does not read as a relation: null when it has no plain
    /// <c>[Navigate]</c>, or when that is not one of the two supported forms with a junction type.
    /// </summary>
    public static NavigateShape? TryOf(PropertyInfo property)
    {
        var data = property.CustomAttributes.FirstOrDefault(a => a.AttributeType == typeof(Navigate));
        var shape = data is null ? null : Recognise(data);
        return shape is { IsJunction: true, JunctionType: null } ? null : shape;
    }

    private static NavigateShape? Recognise(CustomAttributeData data)
    {
        var parameters = data.Constructor.GetParameters().Select(p => p.ParameterType).ToArray();
        var args = data.ConstructorArguments;

        if (parameters.SequenceEqual(ForeignKeyParameters))
            return new NavigateShape
            {
                RelationType = (NavigateType)Enum.ToObject(typeof(NavigateType), args[0].Value!),
                ForeignKey = args[1].Value as string ?? string.Empty
            };
        if (parameters.SequenceEqual(JunctionParameters))
            return new NavigateShape
            {
                IsJunction = true,
                JunctionType = (Type?)args[0].Value,
                ParentKey = args[1].Value as string ?? string.Empty,
                TargetKey = args[2].Value as string ?? string.Empty
            };
        return null;
    }

    private void Validate(PropertyInfo property)
    {
        var isList = IsListProperty(property);
        if (IsJunction) ValidateJunction(property, isList);
        else ValidateForeignKey(property, isList);
    }

    private void ValidateJunction(PropertyInfo property, bool isList)
    {
        if (JunctionType is null) throw Unsupported(property, "the junction type is null");
        if (!isList)
            throw Unsupported(
                property, "the junction form is declared on a single-entity property; it needs a list property");
        if (string.IsNullOrWhiteSpace(ParentKey) || string.IsNullOrWhiteSpace(TargetKey))
            throw Unsupported(property, "a junction key is null, empty or blank");
    }

    private void ValidateForeignKey(PropertyInfo property, bool isList)
    {
        switch (RelationType)
        {
            case NavigateType.OneToOne or NavigateType.ManyToOne when isList:
                throw Unsupported(
                    property, $"NavigateType.{RelationType} is declared on a list property; it needs a single-entity property");
            case NavigateType.OneToMany when !isList:
                throw Unsupported(
                    property, "NavigateType.OneToMany is declared on a single-entity property; it needs a list property");
            case NavigateType.OneToOne or NavigateType.ManyToOne or NavigateType.OneToMany:
                break;
            default:
                throw Unsupported(property, $"NavigateType.{RelationType} is not a relation form the CMS reads");
        }

        if (string.IsNullOrWhiteSpace(ForeignKey))
            throw Unsupported(property, "the foreign key is null, empty or blank");
    }

    private static MetadataException Unsupported(PropertyInfo property, string found) =>
        new($"Relation '{property.DeclaringType?.Name}.{property.Name}' has an unsupported [Navigate]: {found}. " +
            $"Supported: {SupportedForms}.");
}
