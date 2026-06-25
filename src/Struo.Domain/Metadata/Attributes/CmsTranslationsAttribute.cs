namespace Struo.Domain.Metadata.Attributes;

// Defined for the full §4 surface; NOT read by the Phase 1 scanner (translation tables = Phase 4).
[AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = false)]
public sealed class CmsTranslationsAttribute(Type translationEntityType) : Attribute
{
    public Type TranslationEntityType { get; } = translationEntityType;
}
