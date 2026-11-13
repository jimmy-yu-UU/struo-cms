namespace Struo.Infrastructure.Metadata;

/// <summary>The property types the framework stores as a JSON column without an annotation.</summary>
internal static class ListProperty
{
    /// <summary>True for <c>List&lt;T&gt;</c>.</summary>
    internal static bool Is(Type propertyType) =>
        propertyType.IsGenericType && propertyType.GetGenericTypeDefinition() == typeof(List<>);
}
