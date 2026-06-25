namespace Struo.Domain.Metadata.Attributes;

[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class CmsCollectionAttribute(string name) : Attribute
{
    public string Name { get; } = name;
    public string? Label { get; set; }
    public string? Icon { get; set; }
    public string? Group { get; set; }
    public string? DefaultDisplayField { get; set; }
}
