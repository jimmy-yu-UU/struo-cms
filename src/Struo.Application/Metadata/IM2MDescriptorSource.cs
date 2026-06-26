namespace Struo.Application.Metadata;

/// <summary>
/// Carries the junction-table information needed by <see cref="Struo.Application.Query.ItemService"/>
/// to sync M2M assignments after a create or update. Infrastructure populates this via
/// <see cref="Struo.Infrastructure.Metadata.RelationshipGraph"/>.
/// </summary>
public sealed record M2MDescriptor(
    string RelationName,
    string TargetCollection,
    Type   JunctionType,
    string ParentFkProperty,
    string TargetFkProperty,
    string? SortProperty);

/// <summary>
/// Returns the M2M junction descriptors for a collection. Implemented by
/// <c>RelationshipGraph</c> (Infrastructure) and injected into <see cref="Struo.Application.Query.ItemService"/>.
/// </summary>
public interface IM2MDescriptorSource
{
    IReadOnlyList<M2MDescriptor> M2MDescriptors(string collection);
}
