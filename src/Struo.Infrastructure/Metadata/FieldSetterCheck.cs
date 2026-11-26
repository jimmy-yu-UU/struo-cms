using System.Reflection;
using System.Text.Json.Serialization;
using Struo.Domain.Metadata;
using Struo.Domain.Metadata.Attributes;

namespace Struo.Infrastructure.Metadata;

/// <summary>
/// The scan-time check that a writable <c>[CmsField]</c> can receive a value. A write binds a collection
/// field through a public setter, or a non-public one that carries <c>[JsonInclude]</c>; a repeater
/// sub-field the same way, or through the one public constructor of its row type when the property has no
/// setter; a translation field is assigned through any setter its type can reach. A <c>ReadOnly</c>
/// collection field or sub-field is not written, so it needs none.
/// </summary>
internal static class FieldSetterCheck
{
    private const BindingFlags PublicInstance = BindingFlags.Public | BindingFlags.Instance;

    /// <summary>
    /// A writable <c>[CmsField]</c> property of a collection class (<paramref name="type"/>) is bound by a
    /// write: without that the value written to it is not stored.
    /// </summary>
    public static void RequirePublicSetters(Type type) =>
        Require(type, prop => HasPublicSetter(prop) || IsJsonIncluded(prop));

    /// <summary>
    /// A writable <c>[CmsField]</c> property of a repeater row type (<paramref name="type"/>) is bound by a
    /// write, through a setter or a constructor parameter.
    /// </summary>
    public static void RequireRowSetters(Type type) =>
        Require(type, prop => HasPublicSetter(prop) || BoundByConstructor(type, prop));

    /// <summary>
    /// A <c>[CmsField]</c> property of a translation class (<paramref name="type"/>) has a setter it can
    /// reach: the write of a translation value fails without one, <c>ReadOnly</c> or not.
    /// </summary>
    public static void RequireSetters(Type type)
    {
        var unwritable = FieldsOf(type).FirstOrDefault(p => p.SetMethod is null);
        if (unwritable is null) return;

        var owner = unwritable.DeclaringType!;
        throw new MetadataException(SetterOf(unwritable) is not null
            ? $"Field '{NameOf(unwritable)}' has a private setter on the base class {owner.Name}, which " +
              $"{type.Name} cannot reach. Make it protected, init or public, or declare the field on {type.Name}."
            : $"Field '{NameOf(unwritable)}' has no setter, so writing a translation value to it fails. Add a setter.");
    }

    private static void Require(Type type, Func<PropertyInfo, bool> isBound)
    {
        var unwritable = FieldsOf(type).FirstOrDefault(
            p => p.GetCustomAttribute<CmsFieldAttribute>() is { ReadOnly: false } && !isBound(p));
        if (unwritable is null) return;

        throw new MetadataException(SetterOf(unwritable) is { } setter
            ? $"Field '{NameOf(unwritable)}' has a {Access(setter)} setter, which a write does not call, so a " +
              "value written to it is not stored. Make the setter public; or, when server code sets the value, " +
              "declare [CmsField(ReadOnly = true)]."
            : $"Field '{NameOf(unwritable)}' has no setter, so a value written to it is not stored. If the " +
              "server computes the value, declare [CmsField(ReadOnly = true)]; otherwise add a public setter.");
    }

    private static bool HasPublicSetter(PropertyInfo prop) => prop.SetMethod is { IsPublic: true };

    // System.Text.Json binds a non-public setter that carries [JsonInclude], and the ORM reads the value back.
    private static bool IsJsonIncluded(PropertyInfo prop) =>
        prop.SetMethod is not null && prop.IsDefined(typeof(JsonIncludeAttribute));

    // A row without a public parameterless constructor is built through its one public constructor, and
    // a get-only property is bound by the parameter of the same name and type.
    private static bool BoundByConstructor(Type rowType, PropertyInfo prop)
    {
        if (prop.SetMethod is not null) return false;

        var constructors = rowType.GetConstructors();
        return constructors is [{ } only]
               && Array.Exists(only.GetParameters(), p =>
                   string.Equals(p.Name, prop.Name, StringComparison.OrdinalIgnoreCase) && p.ParameterType == prop.PropertyType);
    }

    // A base class's private setter is hidden from the derived type's reflection: SetMethod is null there.
    private static MethodInfo? SetterOf(PropertyInfo prop) =>
        prop.SetMethod ?? prop.DeclaringType!.GetTypeInfo().GetDeclaredProperty(prop.Name)?.SetMethod;

    private static string Access(MethodInfo setter)
    {
        if (setter.IsFamilyOrAssembly) return "protected internal";
        if (setter.IsFamilyAndAssembly) return "private protected";
        if (setter.IsFamily) return "protected";
        return setter.IsAssembly ? "internal" : "private";
    }

    private static IEnumerable<PropertyInfo> FieldsOf(Type type) =>
        type.GetProperties(PublicInstance).Where(p => p.IsDefined(typeof(CmsFieldAttribute), inherit: false));

    private static string NameOf(PropertyInfo prop) => $"{prop.DeclaringType!.Name}.{prop.Name}";
}
