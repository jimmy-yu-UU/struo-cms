namespace Struo.Domain.Metadata.Attributes;

[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class CmsCollectionAttribute(string label) : Attribute
{
    public string Label { get; } = label;
    public string? Icon { get; set; }
    public string? Group { get; set; }
    public string? DefaultDisplayField { get; set; }
}
