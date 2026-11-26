using System.Reflection;
using System.Text.Json.Serialization;
using Struo.Domain.Metadata;
using Struo.Domain.Metadata.Attributes;

namespace Struo.Infrastructure.Metadata;

/// <summary>
/// The scan-time check that a writable <c>[CmsField]</c> can receive a value. A write binds a collection
/// field through a public setter, or a non-public one that carries <c>[JsonInclude]</c>; a repeater
/// sub-field through a public setter, or through a parameter of the same name and type on the one public
/// constructor of its row type; a translation field is assigned through any setter its type can reach. A
/// <c>ReadOnly</c> collection field or sub-field is not written, so it needs none.
/// Why: docs/ai/decisions/cmsfield-is-a-public-instance-property.md
/// </summary>
internal static class FieldSetterCheck
{
    private const BindingFlags PublicInstance = BindingFlags.Public | BindingFlags.Instance;

    private const string ReadOnlyAdvice = "or, when server code sets the value, declare [CmsField(ReadOnly = true)].";

    /// <summary>
    /// A writable <c>[CmsField]</c> property of a collection class (<paramref name="type"/>) is bound by a
    /// write: without that the value written to it is not stored.
    /// </summary>
    public static void RequirePublicSetters(Type type) =>
        Require(type, prop => HasPublicSetter(prop) || IsJsonIncluded(prop), CollectionRefusal);

    /// <summary>
    /// A writable <c>[CmsField]</c> property of a repeater row type (<paramref name="type"/>) is bound by a
    /// write, through a public setter or a constructor parameter.
    /// </summary>
    public static void RequireRowSetters(Type type)
    {
        var constructor = BindingConstructor(type);
        Require(type, prop => HasPublicSetter(prop) || BoundByConstructor(constructor, prop), RowRefusal);
    }

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

    private static void Require(Type type, Func<PropertyInfo, bool> isBound, Func<PropertyInfo, string> refusal)
    {
        var unwritable = FieldsOf(type).FirstOrDefault(
            p => p.GetCustomAttribute<CmsFieldAttribute>() is { ReadOnly: false } && !isBound(p));
        if (unwritable is null) return;

        throw new MetadataException(refusal(unwritable));
    }

    private static string CollectionRefusal(PropertyInfo prop)
    {
        if (SetterOf(prop) is not { } setter) return NoSetter(prop, "otherwise add a public setter.");

        // [JsonInclude] only helps a setter the collection type can reach.
        var remedy = prop.SetMethod is null ? "Make the setter public;" : "Make the setter public or mark it [JsonInclude];";
        return NonPublicSetter(prop, setter, remedy);
    }

    private static string RowRefusal(PropertyInfo prop)
    {
        if (SetterOf(prop) is not { } setter)
        {
            return NoSetter(prop, "otherwise add a public setter, or a parameter of the same name and type on " +
                                  "the row's one public constructor.");
        }

        return IsJsonIncludeDeclared(prop)
            ? $"Field '{NameOf(prop)}' has a {Access(setter)} setter with [JsonInclude]; a repeater row is read " +
              "back without calling it, so the stored value reads back empty. Make the setter public, or take " +
              "the value through a parameter of the row's one public constructor."
            : NonPublicSetter(prop, setter, "Make the setter public;");
    }

    private static string NoSetter(PropertyInfo prop, string otherwise) =>
        $"Field '{NameOf(prop)}' has no setter, so a value written to it is not stored. If the server computes " +
        $"the value, declare [CmsField(ReadOnly = true)]; {otherwise}";

    private static string NonPublicSetter(PropertyInfo prop, MethodInfo setter, string remedy) =>
        $"Field '{NameOf(prop)}' has a {Access(setter)} setter, which a write does not call, so a value " +
        $"written to it is not stored. {remedy} {ReadOnlyAdvice}";

    private static bool HasPublicSetter(PropertyInfo prop) => prop.SetMethod is { IsPublic: true };

    // System.Text.Json binds a non-public setter that carries [JsonInclude] on the property's own
    // declaration, and the ORM reads the value back.
    private static bool IsJsonIncluded(PropertyInfo prop) => prop.SetMethod is not null && IsJsonIncludeDeclared(prop);

    private static bool IsJsonIncludeDeclared(PropertyInfo prop) => prop.IsDefined(typeof(JsonIncludeAttribute), inherit: false);

    // The one public constructor of a row type builds the row, unless another constructor carries
    // [JsonConstructor], which System.Text.Json prefers; null when no constructor binds the row.
    private static ConstructorInfo? BindingConstructor(Type rowType) =>
        rowType.GetConstructors() is [{ } only]
        && !rowType.GetTypeInfo().DeclaredConstructors.Any(c => c != only && c.IsDefined(typeof(JsonConstructorAttribute)))
            ? only
            : null;

    // A property without a public setter is bound by the constructor parameter of the same name and type.
    private static bool BoundByConstructor(ConstructorInfo? constructor, PropertyInfo prop) =>
        constructor is not null
        && Array.Exists(constructor.GetParameters(), p =>
            string.Equals(p.Name, prop.Name, StringComparison.OrdinalIgnoreCase) && p.ParameterType == prop.PropertyType);

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
