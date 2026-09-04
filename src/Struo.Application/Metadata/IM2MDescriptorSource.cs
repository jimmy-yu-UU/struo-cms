namespace Struo.Application.Metadata;

/// <summary>One payload column of a junction collection: the camelCase field name the API exposes and
/// the CLR property it binds to. FK and sort columns are never payload.</summary>
public sealed record JunctionPayloadField(string Name, string Property, bool Hidden);

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
    string? SortProperty,
    string? JunctionCollection = null,
    IReadOnlyList<JunctionPayloadField>? JunctionPayload = null)
{
    /// <summary>True when <see cref="JunctionCollection"/> carries payload fields beyond its FKs and
    /// sort column — i.e. the junction is itself a [CmsCollection] with extra data on it.</summary>
    public bool HasPayload => JunctionPayload is { Count: > 0 };
}

/// <summary>
/// An M2M descriptor viewed from the TARGET side: <paramref name="SourceCollection"/> is the
/// collection that declares the relation (the "owning"/parent side), and <see cref="Descriptor"/>
/// is its normal (parent-side) M2M descriptor. Used by purge to find and clean
/// up junction rows for a relation the purge target does not itself declare — e.g. purging a Tag
/// must delete the Article-side <c>article_tags</c> rows whose <c>TagId</c> points at it, even
/// though the M2M relation is declared on Article, not Tag.
/// </summary>
public sealed record InboundM2MDescriptor(string SourceCollection, M2MDescriptor Descriptor);

/// <summary>
/// Returns the M2M junction descriptors for a collection. Implemented by
/// <c>RelationshipGraph</c> (Infrastructure) and injected into <see cref="Struo.Application.Query.ItemService"/>.
/// </summary>
public interface IM2MDescriptorSource
{
    IReadOnlyList<M2MDescriptor> M2MDescriptors(string collection);

    /// <summary>
    /// M2M descriptors declared on OTHER collections whose target is <paramref name="targetCollection"/>.
    /// Default empty for any implementer that predates this addition.
    /// </summary>
    IReadOnlyList<InboundM2MDescriptor> InboundM2MDescriptors(string targetCollection) => [];
}
