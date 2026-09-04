using Struo.Domain.Metadata.Enums;

namespace Struo.Domain.Metadata.Models;

public sealed record RelationMetadata
{
    public required string Name { get; init; }
    public required string Label { get; init; }
    public required RelationKind Kind { get; init; }
    public required string TargetCollection { get; init; }
    public required RelationInterface Interface { get; init; }
    public string? ForeignKey { get; init; }
    public string? DisplayTemplate { get; init; }
    public string? PickerQuery { get; init; }
    public OnDelete OnDelete { get; init; }
    public bool Editable { get; init; }
    public bool SelfReferencing { get; init; }

    /// <summary>camelCase name of the junction collection when the M2M junction type is itself a
    /// [CmsCollection]; null for plain junction entities and for non-M2M relations.</summary>
    public string? JunctionCollection { get; init; }
}
