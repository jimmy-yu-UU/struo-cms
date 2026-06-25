namespace Struo.Domain.Metadata.Attributes;

[AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
public sealed class CmsFieldGroupAttribute(string name) : Attribute
{
    public string Name { get; } = name;
    public string? Label { get; set; }
    public int Sort { get; set; }
}
