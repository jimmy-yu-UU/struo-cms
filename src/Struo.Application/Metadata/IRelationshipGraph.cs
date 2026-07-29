using Struo.Domain.Metadata.Models;

namespace Struo.Application.Metadata;

public interface IRelationshipGraph
{
    IReadOnlyList<RelationMetadata> Relations(string collection);
    RelationMetadata? Resolve(string collection, string relationName);
    IReadOnlyList<(string SourceCollection, string ForeignKey)> InboundRestrict(string targetCollection);

    /// <summary>
    /// Inbound many-to-one relations targeting <paramref name="targetCollection"/> whose
    /// <c>OnDelete</c> is <see cref="Struo.Domain.Metadata.Enums.OnDelete.SetNull"/>.
    /// Default empty for any implementer that predates this addition (e.g. test fakes) — purge simply
    /// performs no SetNull work for them.
    /// </summary>
    IReadOnlyList<(string SourceCollection, string ForeignKey)> InboundSetNull(string targetCollection) => [];

    /// <summary>
    /// Inbound many-to-one relations targeting <paramref name="targetCollection"/> whose
    /// <c>OnDelete</c> is <see cref="Struo.Domain.Metadata.Enums.OnDelete.Cascade"/>.
    /// Default empty — see <see cref="InboundSetNull"/>.
    /// </summary>
    IReadOnlyList<(string SourceCollection, string ForeignKey)> InboundCascade(string targetCollection) => [];
}
