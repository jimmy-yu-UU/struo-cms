namespace Struo.Domain.Metadata.Models;

public sealed record CollectionMetadata
{
    public required string Name { get; init; }
    public required string Label { get; init; }
    public string? Icon { get; init; }
    public string? Group { get; init; }
    public string? DefaultDisplayField { get; init; }

    /// <summary>
    /// When true, generic-CRUD writes (create/update/delete) require a super-admin regardless of
    /// per-collection RBAC grants. Guards the identity/authorization tables against privilege
    /// escalation. See <c>CmsCollectionAttribute.AdminOnly</c>.
    /// </summary>
    public bool AdminOnly { get; init; }
    public required IReadOnlyList<FieldGroupMetadata> FieldGroups { get; init; }
    public required IReadOnlyList<FieldMetadata> Fields { get; init; }
    public IReadOnlyList<RelationMetadata> Relations { get; init; } = [];

    /// <summary>Translation sidecar metadata, or null when the collection has no translatable fields.</summary>
    public TranslationMetadata? Translation { get; init; }
}
