using Struo.Domain.Metadata.Enums;

namespace Struo.Domain.Metadata.Models;

public sealed record FieldMetadata
{
    public required string Name { get; init; }
    public required string Label { get; init; }
    public required FieldInterface Interface { get; init; }
    public bool Required { get; init; }
    public bool Searchable { get; init; }
    public bool Sortable { get; init; }
    public bool ReadOnly { get; init; }
    public bool Hidden { get; init; }
    public bool Translatable { get; init; }
    public int Sort { get; init; }
    public string? HelpText { get; init; }
    public string? Group { get; init; }
    public IReadOnlyList<FieldOption>? Options { get; init; }
    public bool IsSystem { get; init; }
}
