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

    /// <summary>Omit from the admin sidebar/nav (presentation only). See CmsCollectionAttribute.Hidden.</summary>
    public bool Hidden { get; init; }

    /// <summary>
    /// When true, the collection's entity implements <see cref="Struo.Domain.Auditing.ISoftDeletable"/>:
    /// DELETE marks the row (DeletedAt set) instead of removing it, reads exclude it by default, and a
    /// restore/purge surface applies. Derived by the scanner from the interface — no attribute.
    /// </summary>
    public bool SoftDelete { get; init; }

    /// <summary>
    /// When true, the collection is revisioned: create/update append a snapshot to the `revisions` table
    /// and a revert surface applies. Derived by the scanner from `[CmsCollection(Revisions=true)]`.
    /// </summary>
    public bool Revisions { get; init; }
    public required IReadOnlyList<FieldGroupMetadata> FieldGroups { get; init; }
    public required IReadOnlyList<FieldMetadata> Fields { get; init; }
    public IReadOnlyList<RelationMetadata> Relations { get; init; } = [];

    /// <summary>Translation sidecar metadata, or null when the collection has no translatable fields.</summary>
    public TranslationMetadata? Translation { get; init; }
}
