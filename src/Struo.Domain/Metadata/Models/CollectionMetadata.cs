namespace Struo.Domain.Metadata.Models;

public sealed record CollectionMetadata
{
    public required string Name { get; init; }
    public required string Label { get; init; }
    public string? Icon { get; init; }
    public string? Group { get; init; }
    public string? DefaultDisplayField { get; init; }
    public required IReadOnlyList<FieldGroupMetadata> FieldGroups { get; init; }
    public required IReadOnlyList<FieldMetadata> Fields { get; init; }
    public IReadOnlyList<RelationMetadata> Relations { get; init; } = [];
}
