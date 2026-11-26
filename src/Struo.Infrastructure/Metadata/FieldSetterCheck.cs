using System.Reflection;
using Struo.Domain.Metadata;
using Struo.Domain.Metadata.Attributes;

namespace Struo.Infrastructure.Metadata;

/// <summary>
/// The scan-time check that a writable <c>[CmsField]</c> can receive a value. A collection field and a
/// repeater sub-field are bound through a public setter; a translation field is written through any setter.
/// A <c>ReadOnly</c> field is not written, so it needs none.
/// </summary>
internal static class FieldSetterCheck
{
    private const BindingFlags PublicInstance = BindingFlags.Public | BindingFlags.Instance;

    /// <summary>
    /// A writable <c>[CmsField]</c> property of a collection class or repeater child type
    /// (<paramref name="type"/>) has a public setter: without one the value written to it is not stored.
    /// </summary>
    public static void RequirePublicSetters(Type type)
    {
        var unwritable = FieldsOf(type).FirstOrDefault(
            p => p.GetCustomAttribute<CmsFieldAttribute>() is { ReadOnly: false } && p.SetMethod is not { IsPublic: true });
        if (unwritable is null) return;

        throw new MetadataException(
            $"Field '{NameOf(unwritable)}' is writable but has no public setter, so a value written to it is " +
            "not stored. Add a public setter, or declare [CmsField(ReadOnly = true)].");
    }

    /// <summary>
    /// A <c>[CmsField]</c> property of a translation class (<paramref name="type"/>) has a setter: the write
    /// of a translation value fails without one, <c>ReadOnly</c> or not. A setter of any accessibility stores.
    /// </summary>
    public static void RequireSetters(Type type)
    {
        var unwritable = FieldsOf(type).FirstOrDefault(p => p.SetMethod is null);
        if (unwritable is null) return;

        throw new MetadataException(
            $"Field '{NameOf(unwritable)}' has no setter, so writing a translation value to it fails. Add a setter.");
    }

    private static IEnumerable<PropertyInfo> FieldsOf(Type type) =>
        type.GetProperties(PublicInstance).Where(p => p.IsDefined(typeof(CmsFieldAttribute), inherit: false));

    private static string NameOf(PropertyInfo prop) => $"{prop.DeclaringType!.Name}.{prop.Name}";
}
