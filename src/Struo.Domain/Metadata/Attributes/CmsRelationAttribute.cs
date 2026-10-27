using Struo.Domain.Metadata.Enums;

namespace Struo.Domain.Metadata.Attributes;

// Declares the relation-configuration surface (interface, display template, picker query, sort
// field, cascade behavior, editability); read by MetadataScanner to build relation metadata.
// DisplayColumns and MaxDepth are not read by anything and are obsolete.
[AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = false)]
public sealed class CmsRelationAttribute : Attribute
{
    public RelationInterface Interface { get; set; } = RelationInterface.Dropdown;
    public string? DisplayTemplate { get; set; }
    public string? PickerQuery { get; set; }
    public string? SortField { get; set; }
    public OnDelete OnDelete { get; set; } = OnDelete.Restrict;
    public bool Editable { get; set; } = true;
    [Obsolete("Has no effect; removed in the next minor version.")]
    public string? DisplayColumns { get; set; }

    [Obsolete("Has no effect; removed in the next minor version.")]
    public int MaxDepth { get; set; } = 1;
}
