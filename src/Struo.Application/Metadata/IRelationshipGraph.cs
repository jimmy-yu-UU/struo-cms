using Struo.Domain.Metadata.Models;

namespace Struo.Application.Metadata;

public interface IRelationshipGraph
{
    IReadOnlyList<RelationMetadata> Relations(string collection);
    RelationMetadata? Resolve(string collection, string relationName);
    IReadOnlyList<(string SourceCollection, string ForeignKey)> InboundRestrict(string targetCollection);
}
