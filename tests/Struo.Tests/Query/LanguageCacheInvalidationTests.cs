// tests/Struo.Tests/Query/LanguageCacheInvalidationTests.cs
using AwesomeAssertions;
using Struo.Application.Configuration;
using Struo.Application.Localization;
using Struo.Application.Metadata;
using Struo.Application.Query;
using Struo.Application.Query.Write;
using Struo.Application.Revisions;
using Struo.Application.Security;
using Struo.Domain.Auditing;
using Struo.Domain.Localization;
using Struo.Domain.Metadata.Models;
using Struo.Domain.Query;
using Struo.Infrastructure.Security;
using Struo.Tests.Support;
using Xunit;

namespace Struo.Tests.Query;

/// <summary>
/// Pins that ItemService invalidates the language cache on trash, purge AND restore of a `language`
/// row, not just create/update. The gate keys off the collection NAME ("language"), so this
/// fixture entity implements <see cref="ISoftDeletable"/> itself purely to make the trash/restore
/// branches reachable here — the real, production `Language` row does not implement it (languages
/// are never soft-deleted in this template; every delete purges), which is also covered below.
/// Minimal hand-rolled stubs throughout (no DB, no SqlSugar) — same style as
/// <c>DeleteRestrictWithGuidPkTests</c> — so the only real code under test is ItemService itself.
/// </summary>
public class LanguageCacheInvalidationTests
{
    private const string Collection = "language";

    private sealed class FakeLanguageRow : ISoftDeletable
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public DateTime? DeletedAt { get; set; }
        public Guid? DeletedBy { get; set; }
    }

    private sealed class StubGraph : IRelationshipGraph
    {
        public IReadOnlyList<RelationMetadata> Relations(string c) => [];
        public RelationMetadata? Resolve(string c, string r) => null;
        public IReadOnlyList<(string SourceCollection, string ForeignKey)> InboundRestrict(string t) => [];
    }

    private sealed class StubRegistry(EntityDescriptor descriptor) : IEntityRegistry
    {
        public EntityDescriptor? Get(string c) =>
            string.Equals(c, Collection, StringComparison.OrdinalIgnoreCase) ? descriptor : null;
    }

    private sealed class StubMeta(bool softDelete) : IMetadataProvider
    {
        public IReadOnlyList<CollectionMetadata> GetCollections() => [];
        public CollectionMetadata? GetCollection(string c) =>
            string.Equals(c, Collection, StringComparison.OrdinalIgnoreCase)
                ? new CollectionMetadata
                {
                    Name = Collection, Label = Collection, FieldGroups = [], Fields = [],
                    SoftDelete = softDelete,
                }
                : null;
    }

    private sealed class StubM2M : IM2MDescriptorSource
    {
        public IReadOnlyList<M2MDescriptor> M2MDescriptors(string c) => [];
    }

    private sealed class StubExpander : IRelationExpander
    {
        public Task<Dictionary<object, Dictionary<string, object?>>> ExpandAsync(
            string c, IReadOnlyList<object> parents, DeepSpec deep,
            Func<string, object, IReadOnlyList<string>?, IReadOnlyDictionary<string, object?>> project,
            Func<object, object> parentId, Func<object, string, object?> readProp,
            string? locale = null, Func<string, bool>? canReadJunction = null, CancellationToken ct = default) =>
            Task.FromResult(new Dictionary<object, Dictionary<string, object?>>());
    }

    private sealed class StubPermissions : IPermissionService
    {
        public bool CanRead(string c) => true;
        public bool CanWrite(string c) => true;
        public bool CanDelete(string c) => true;
        public IReadOnlyCollection<string> ReadableFields(string c, IEnumerable<string> all) => all.ToList();
    }

    private sealed class StubRevisionStore : IRevisionStore
    {
        public Task CaptureAsync(string collection, string itemId, string operation, string snapshotJson,
            long? sourceRevisionNumber = null, CancellationToken ct = default) =>
            throw new InvalidOperationException("Not expected to be called: the fixture collection is not revisioned.");
        public Task<IReadOnlyList<RevisionInfo>> ListAsync(string collection, string itemId, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<RevisionInfo>>([]);
        public Task<RevisionRecord?> GetAsync(string collection, string itemId, long revisionNumber, CancellationToken ct = default) =>
            Task.FromResult<RevisionRecord?>(null);
    }

    // Records every Invalidate() call so a test can assert exactly how many happened; the other
    // members are never exercised by the paths under test here.
    private sealed class RecordingLanguages : ILanguageProvider
    {
        public int InvalidateCalls { get; private set; }
        public IReadOnlyList<LanguageInfo> Enabled() => [];
        public string DefaultCode() => "en";
        public bool IsEnabled(string code) => true;
        public void Invalidate() => InvalidateCalls++;
    }

    private sealed class StubRepo(object? row, bool deleteReturns, bool softDeleteReturns, bool restoreReturns)
        : IItemRepository
    {
        public Task<QueryResult> QueryAsync(string c, QueryModel q, IReadOnlyList<string> s, string? l, DeletedFilter deleted, CancellationToken ct) =>
            Task.FromResult(new QueryResult([], 0));
        public Task<object?> GetByIdAsync(string c, string id, DeletedFilter deleted, CancellationToken ct) =>
            Task.FromResult(row);
        public Task<object> CreateAsync(string c, object e, CancellationToken ct) => Task.FromResult(e);
        public Task<object?> UpdateAsync(string c, string id, object e, CancellationToken ct) => Task.FromResult<object?>(e);
        public Task<bool> DeleteAsync(string c, string id, CancellationToken ct) => Task.FromResult(deleteReturns);
        public Task<bool> SoftDeleteAsync(string c, string id, DateTime deletedAt, Guid? deletedBy, CancellationToken ct) =>
            Task.FromResult(softDeleteReturns);
        public Task<bool> RestoreAsync(string c, string id, CancellationToken ct) => Task.FromResult(restoreReturns);
        public Task InTransactionAsync(Func<Task> body, CancellationToken ct) => body();
        public Task<T> InTransactionAsync<T>(Func<Task<T>> body, CancellationToken ct) => body();
        public Task<IReadOnlyList<object>> QueryWhereInAsync(string c, string prop, IReadOnlyList<object> vals, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<object>>([]);
        public Task<IReadOnlyList<object>> QueryWhereInFilteredAsync(string c, string prop, IReadOnlyList<object> vals, FilterNode? extraFilter, string? queryLocale, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<object>>([]);
        public Task<IReadOnlyList<object>> QueryEntityWhereInAsync(Type t, string p, IReadOnlyList<object> vals, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<object>>([]);
        public Task SyncManyToManyAsync(Type jt, string pfk, string tfk, string? sort, object pid, IReadOnlyList<JunctionLink> links, CancellationToken ct) =>
            Task.CompletedTask;
        public Task<IReadOnlyList<object>> LoadTranslationsAsync(Type tt, string fk, string lp, IReadOnlyList<object> pids, string? locale, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<object>>([]);
        public Task SyncTranslationsAsync(Type tt, string fk, string lp, IReadOnlyList<string> fp, object pid, IReadOnlyDictionary<string, IReadOnlyDictionary<string, object?>> pl, CancellationToken ct) =>
            Task.CompletedTask;
    }

    private static EntityDescriptor Descriptor() =>
        new(typeof(FakeLanguageRow),
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["id"] = "Id" },
            "Id");

    private static ItemService BuildService(StubRepo repo, StubMeta meta, RecordingLanguages languages)
    {
        var registry = new StubRegistry(Descriptor());
        var graph = new StubGraph();
        var m2m = new StubM2M();
        return new ItemService(
            repo, meta, registry, new StubPermissions(),
            graph, new StubExpander(), m2m,
            languages,
            new StruoQueryOptions(), new GanssHtmlSanitizer(),
            new TestCurrentUserAccessor(Guid.Empty),
            new StubRevisionStore(), new RevisionSnapshotBuilder(repo, meta, registry, m2m),
            new NoopUserSessionRevocationService());
    }

    [Fact]
    public async Task Trashing_a_language_row_invalidates_the_cache()
    {
        var row = new FakeLanguageRow();
        var languages = new RecordingLanguages();
        var repo = new StubRepo(row, deleteReturns: false, softDeleteReturns: true, restoreReturns: false);
        var svc = BuildService(repo, new StubMeta(softDelete: true), languages);

        var ok = await svc.DeleteAsync(Collection, row.Id.ToString(), purge: false);

        ok.Should().BeTrue();
        languages.InvalidateCalls.Should().Be(1);
    }

    [Fact]
    public async Task Purging_a_language_row_invalidates_the_cache()
    {
        // The real `Language` entity is never soft-deletable (SoftDelete: false here reproduces
        // that), so every delete of a real language row takes exactly this branch regardless of the
        // caller's `purge` flag.
        var row = new FakeLanguageRow();
        var languages = new RecordingLanguages();
        var repo = new StubRepo(row, deleteReturns: true, softDeleteReturns: false, restoreReturns: false);
        var svc = BuildService(repo, new StubMeta(softDelete: false), languages);

        var ok = await svc.DeleteAsync(Collection, row.Id.ToString(), purge: true);

        ok.Should().BeTrue();
        languages.InvalidateCalls.Should().Be(1);
    }

    [Fact]
    public async Task Restoring_a_language_row_invalidates_the_cache()
    {
        var row = new FakeLanguageRow { DeletedAt = DateTime.UtcNow, DeletedBy = Guid.NewGuid() };
        var languages = new RecordingLanguages();
        var repo = new StubRepo(row, deleteReturns: false, softDeleteReturns: false, restoreReturns: true);
        var svc = BuildService(repo, new StubMeta(softDelete: true), languages);

        var result = await svc.RestoreAsync(Collection, row.Id.ToString());

        result.Should().NotBeNull();
        languages.InvalidateCalls.Should().Be(1);
    }

    [Fact]
    public async Task A_purge_that_finds_nothing_to_delete_does_not_invalidate_the_cache()
    {
        var row = new FakeLanguageRow();
        var languages = new RecordingLanguages();
        var repo = new StubRepo(row, deleteReturns: false, softDeleteReturns: false, restoreReturns: false);
        var svc = BuildService(repo, new StubMeta(softDelete: false), languages);

        var ok = await svc.DeleteAsync(Collection, row.Id.ToString(), purge: true);

        ok.Should().BeFalse();
        languages.InvalidateCalls.Should().Be(0);
    }

    [Fact]
    public async Task A_restore_of_an_already_live_row_does_not_invalidate_the_cache()
    {
        var row = new FakeLanguageRow(); // DeletedAt is already null: nothing to restore
        var languages = new RecordingLanguages();
        var repo = new StubRepo(row, deleteReturns: false, softDeleteReturns: false, restoreReturns: false);
        var svc = BuildService(repo, new StubMeta(softDelete: true), languages);

        var result = await svc.RestoreAsync(Collection, row.Id.ToString());

        result.Should().NotBeNull();
        languages.InvalidateCalls.Should().Be(0);
    }
}
