using Struo.Application.Metadata;
using Struo.Application.Query;
using Struo.Application.Revisions;
using Struo.Domain.Metadata.Models;

namespace Struo.Tests.Support;

/// <summary>Builds the reference-handling pipeline a <c>FileService</c> needs for trash and purge in
/// unit harnesses that have no M2M junctions and no revisioned collections.</summary>
internal static class FileTestPipeline
{
    private sealed class NoM2M : IM2MDescriptorSource
    {
        public IReadOnlyList<M2MDescriptor> M2MDescriptors(string collection) => [];
    }

    private sealed class NoRevisions : IRevisionStore
    {
        public Task CaptureAsync(string collection, string itemId, string operation, string snapshotJson,
            long? sourceRevisionNumber = null, CancellationToken ct = default) => Task.CompletedTask;
        public Task<IReadOnlyList<RevisionInfo>> ListAsync(string collection, string itemId, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<RevisionInfo>>([]);
        public Task<RevisionRecord?> GetAsync(string collection, string itemId, long revisionNumber, CancellationToken ct = default) =>
            Task.FromResult<RevisionRecord?>(null);
    }

    public static ItemPurgePipeline Create(
        IItemRepository repository, IMetadataProvider metadata, IEntityRegistry registry, IRelationshipGraph graph) =>
        new(repository, metadata, registry, graph, new NoM2M(), new NoRevisions());
}
