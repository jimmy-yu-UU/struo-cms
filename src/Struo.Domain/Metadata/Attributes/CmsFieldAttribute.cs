using Struo.Domain.Metadata.Enums;

namespace Struo.Domain.Metadata.Attributes;

[AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = false)]
public sealed class CmsFieldAttribute : Attribute
{
    public string? Label { get; set; }
    public FieldInterface Interface { get; set; } = FieldInterface.Text;
    public bool Required { get; set; }
    public bool Searchable { get; set; }
    public bool Sortable { get; set; }
    public int Sort { get; set; }
    public bool ReadOnly { get; set; }
    public bool Hidden { get; set; }
    public string? HelpText { get; set; }
    public bool Translatable { get; set; }
    public string? Group { get; set; }

    /// <summary>
    /// Input-length limit (UTF-16 code units), enforced on write. 0 = unset: a string field takes its
    /// column's width (255, or [SugarColumn(Length = n)]), and has no limit when its column states none
    /// (JSON interfaces, a column shape, IsJson, a content-bearing interface without an explicit
    /// ColumnDataType, an explicit ColumnDataType without a Length). An explicit ColumnDataType with a
    /// Length is that Length wide, whatever the interface.
    /// An explicit value does not change the column, and one wider than the column's width fails the
    /// metadata scan; a Repeater sub-field has no column and is not checked.
    /// </summary>
    public int MaxLength { get; set; }
}
