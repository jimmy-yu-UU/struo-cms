using System.Text.Json.Serialization;

namespace Struo.Domain.Metadata.Models;

/// <summary>
/// Describes the sidecar translation entity for a collection: where translated rows live, how they
/// link back to the parent, the locale discriminator, and which (camelCase) fields are translatable.
/// </summary>
public sealed record TranslationMetadata
{
    // The CLR Type is an implementation detail used by the runtime (read/write overlay); it must
    // never be JSON-serialized into the public /api/schema surface (a Type graph is cyclic/huge).
    // Not `required`: STJ rejects a `required` member that is also [JsonIgnore]. The scanner always
    // sets it via the object initializer, so it is effectively non-null in practice.
    [JsonIgnore]
    public Type TranslationEntityType { get; init; } = typeof(object);
    public required string ForeignKeyProperty { get; init; }   // CLR name, e.g. "ArticleId"
    public required string LocaleProperty { get; init; }       // CLR name, e.g. "Locale"
    public required IReadOnlyList<string> Fields { get; init; } // camelCase translatable field names
}
