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
    /// (content-bearing and JSON interfaces, a column shape, IsJson, an explicit ColumnDataType).
    /// An explicit value does not change the column.
    /// </summary>
    public int MaxLength { get; set; }
}
