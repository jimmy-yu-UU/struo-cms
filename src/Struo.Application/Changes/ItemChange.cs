namespace Struo.Application.Changes;

/// <param name="Collection">Canonical collection name (<c>CollectionMetadata.Name</c>, e.g. <c>article</c>), never the raw route segment.</param>
/// <param name="Id">The primary key's string form (Guids lower-case).</param>
public sealed record ItemChange(string Collection, string Id, ItemChangeKind Kind);
