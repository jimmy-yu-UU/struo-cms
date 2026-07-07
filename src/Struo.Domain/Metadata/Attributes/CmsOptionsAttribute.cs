namespace Struo.Domain.Metadata.Attributes;

[AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = false)]
public sealed class CmsOptionsAttribute(params string[] options) : Attribute
{
    // Each entry is "value:label", or bare "value" (label defaults to the value).
    public IReadOnlyList<string> Options { get; } = options;
}
