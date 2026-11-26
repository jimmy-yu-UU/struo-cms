using System.Reflection;
using Struo.Domain.Metadata;
using Struo.Domain.Metadata.Attributes;

namespace Struo.Infrastructure.Metadata;

/// <summary>
/// The scan-time check that a translation class declares no <c>[CmsField(ReadOnly = true)]</c>. A
/// <c>translations</c> payload writes every translatable field on create and on update without a read-only
/// check, so <c>ReadOnly</c> on one only changes what the schema reports and what GraphQL leaves out of its
/// translations input.
/// </summary>
internal static class TranslationReadOnlyCheck
{
    /// <summary>Refuses a <c>ReadOnly</c> <c>[CmsField]</c> property of the translation class <paramref name="type"/>.</summary>
    public static void Require(Type type)
    {
        var readOnly = type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .FirstOrDefault(p => p.GetCustomAttribute<CmsFieldAttribute>() is { ReadOnly: true });
        if (readOnly is null) return;

        throw new MetadataException(
            $"Translation field '{readOnly.DeclaringType!.Name}.{readOnly.Name}' declares ReadOnly = true, but a " +
            "translations payload writes it on create and on update, so ReadOnly only changes what the schema " +
            "reports. Remove ReadOnly = true, or declare the property on the parent entity when server code " +
            "owns the value.");
    }
}
