namespace Struo.Domain.Metadata.Attributes;

// Defined for the full §4 surface; read by MetadataScanner to build translation-sidecar metadata.
[AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = false)]
public sealed class CmsTranslationsAttribute(Type translationEntityType) : Attribute
{
    public Type TranslationEntityType { get; } = translationEntityType;
}
