namespace Struo.Domain.Metadata.Models;

/// <summary>
/// Describes the sidecar translation entity for a collection: where translated rows live, how they
/// link back to the parent, the locale discriminator, and which (camelCase) fields are translatable.
/// </summary>
public sealed record TranslationMetadata
{
    public required Type TranslationEntityType { get; init; }
    public required string ForeignKeyProperty { get; init; }   // CLR name, e.g. "ArticleId"
    public required string LocaleProperty { get; init; }       // CLR name, e.g. "Locale"
    public required IReadOnlyList<string> Fields { get; init; } // camelCase translatable field names
}
