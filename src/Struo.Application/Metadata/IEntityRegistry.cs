namespace Struo.Application.Metadata;

public sealed record EntityDescriptor(
    Type EntityType,
    IReadOnlyDictionary<string, string> FieldToProperty,  // camelCase field name -> CLR property name
    string IdProperty);

public interface IEntityRegistry
{
    EntityDescriptor? Get(string collection);  // case-insensitive
}
