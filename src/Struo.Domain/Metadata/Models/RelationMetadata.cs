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

    /// <summary>camelCase name of the junction column that orders the target rows
    /// (<c>[CmsRelation(SortField = ...)]</c>); null when unset or for non-M2M relations.</summary>
    public string? SortField { get; init; }

    /// <summary>camelCase names of the junction collection's payload fields — its non-system, non-readonly
    /// <c>[CmsField]</c>s other than the two foreign keys and <see cref="SortField"/> — hidden ones included
    /// (a client filters on the junction collection's own field metadata). Null unless
    /// <see cref="JunctionCollection"/> is set and at least one such field exists. Computed once by
    /// <c>MetadataScanner</c>; <c>RelationshipGraph</c> resolves CLR properties from this list rather than
    /// re-deriving it.</summary>
    public IReadOnlyList<string>? JunctionPayloadFields { get; init; }
}
