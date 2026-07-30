namespace Struo.Domain.Metadata.Attributes;

// Declares which entity type carries the per-locale translation sidecar; read by MetadataScanner
// to build translation-sidecar metadata.
[AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = false)]
public sealed class CmsTranslationsAttribute(Type translationEntityType) : Attribute
{
    public Type TranslationEntityType { get; } = translationEntityType;
}
