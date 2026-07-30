using Struo.Domain.Metadata.Enums;

namespace Struo.Domain.Metadata.Attributes;

// Declares the complete relation-configuration surface (interface, display, picker query, sort
// field, cascade behavior, depth); read by MetadataScanner to build relation metadata.
[AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = false)]
public sealed class CmsRelationAttribute : Attribute
{
    public RelationInterface Interface { get; set; } = RelationInterface.Dropdown;
    public string? DisplayTemplate { get; set; }
    public string? PickerQuery { get; set; }
    public string? SortField { get; set; }
    public OnDelete OnDelete { get; set; } = OnDelete.Restrict;
    public bool Editable { get; set; } = true;
    public string? DisplayColumns { get; set; }
    public int MaxDepth { get; set; } = 1;
}
