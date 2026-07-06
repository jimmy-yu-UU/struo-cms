using Struo.Domain.Metadata.Enums;

namespace Struo.Domain.Metadata.Attributes;

[AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = false)]
public sealed class CmsFieldAttribute : Attribute
{
    public string? Label { get; set; }
    public FieldInterface Interface { get; set; } = FieldInterface.Text;
    public string? Display { get; set; }
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
    /// CMS-layer input-length limit (UTF-16 code units). 0 = unset: short-string interfaces
    /// default to 255 (matching SqlSugar's default column width), content-bearing interfaces
    /// are unlimited. Independent of the DB column width ([SugarColumn(Length = n)]).
    /// </summary>
    public int MaxLength { get; set; }
}
