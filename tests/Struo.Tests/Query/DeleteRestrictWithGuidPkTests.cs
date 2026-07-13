// tests/Struo.Tests/Query/DeleteRestrictWithGuidPkTests.cs
using AwesomeAssertions;
using Struo.Application.Configuration;
using Struo.Application.Localization;
using Struo.Application.Metadata;
using Struo.Application.Query;
using Struo.Application.Security;
using Struo.Domain.Localization;
using Struo.Domain.Metadata.Models;
using Struo.Domain.Query;
using Struo.Infrastructure.Security;
using Xunit;

namespace Struo.Tests.Query;

/// <summary>
/// Focused unit test verifying that DeleteAsync enforces OnDelete.Restrict for a Guid-PK
/// collection without throwing a spurious 400 (the Convert.ChangeType bug fixed in Task 3).
/// Uses minimal stubs — no DB, no SqlSugar — so the only real code under test is
/// ItemService.DeleteAsync + IdParsing.ParseTo.
/// </summary>
public class DeleteRestrictWithGuidPkTests
{
    // Minimal entity type whose PK is a Guid — stands in for any real Guid-PK collection.
    private sealed class GuidEntity { public Guid Id { get; set; } }

    // ── stubs ────────────────────────────────────────────────────────────────

    private sealed class StubGraph(
        string targetCollection,
        string sourceCollection,
        string foreignKey) : IRelationshipGraph
    {
        public IReadOnlyList<RelationMetadata> Relations(string c) => [];
        public RelationMetadata? Resolve(string c, string r) => null;
        public IReadOnlyList<(string SourceCollection, string ForeignKey)> InboundRestrict(
            string t) =>
            string.Equals(t, targetCollection, StringComparison.OrdinalIgnoreCase)
                ? [(sourceCollection, foreignKey)]
                : [];
    }

    private sealed class StubRegistry(string collection, EntityDescriptor descriptor) : IEntityRegistry
    {
        public EntityDescriptor? Get(string c) =>
            string.Equals(c, collection, StringComparison.OrdinalIgnoreCase) ? descriptor : null;
    }

    // Repository that returns one stub row for QueryWhereInAsync (simulates a referencing row).
    private sealed class StubRepo : IItemRepository
    {
        public Task<QueryResult> QueryAsync(string c, QueryModel q, IReadOnlyList<string> s, string? l, CancellationToken ct) =>
            Task.FromResult(new QueryResult([], 0));
        public Task<object?> GetByIdAsync(string c, string id, CancellationToken ct) =>
            Task.FromResult<object?>(null);
        public Task<object> CreateAsync(string c, object e, CancellationToken ct) =>
            Task.FromResult(e);
        public Task<object?> UpdateAsync(string c, string id, object e, CancellationToken ct) =>
            Task.FromResult<object?>(e);
        public Task<bool> DeleteAsync(string c, string id, CancellationToken ct) =>
            Task.FromResult(true);
        public Task InTransactionAsync(Func<Task> body, CancellationToken ct) => body();
        public Task<IReadOnlyList<object>> QueryWhereInAsync(string c, string prop, IReadOnlyList<object> vals, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<object>>([new GuidEntity()]);
        public Task<IReadOnlyList<object>> QueryWhereInFilteredAsync(string c, string prop, IReadOnlyList<object> vals, FilterNode? extraFilter, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<object>>([new GuidEntity()]);
        public Task<IReadOnlyList<object>> QueryEntityWhereInAsync(Type t, string p, IReadOnlyList<object> vals, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<object>>([]);
        public Task<IReadOnlyList<object>> QueryIdsAsync(string c, FilterNode f, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<object>>([]);
        public Task SyncManyToManyAsync(Type jt, string pfk, string tfk, string? sort, object pid, IReadOnlyList<object> tids, CancellationToken ct) =>
            Task.CompletedTask;
        public Task<IReadOnlyList<object>> LoadTranslationsAsync(Type tt, string fk, string lp, IReadOnlyList<object> pids, string? locale, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<object>>([]);
        public Task<IReadOnlyList<object>> QueryTranslationParentIdsAsync(Type tt, string fk, string lp, string locale, FilterNode fc, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<object>>([]);
        public Task SyncTranslationsAsync(Type tt, string fk, string lp, IReadOnlyList<string> fp, object pid, IReadOnlyDictionary<string, IReadOnlyDictionary<string, object?>> pl, CancellationToken ct) =>
            Task.CompletedTask;
    }

    private sealed class StubMeta(string collection) : IMetadataProvider
    {
        public IReadOnlyList<CollectionMetadata> GetCollections() => [];
        public CollectionMetadata? GetCollection(string c) =>
            string.Equals(c, collection, StringComparison.OrdinalIgnoreCase)
                ? new CollectionMetadata { Name = c, Label = c, FieldGroups = [], Fields = [] }
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
            Func<object, object> parentId, Func<object, string, object?> readProp, CancellationToken ct) =>
            Task.FromResult(new Dictionary<object, Dictionary<string, object?>>());
    }

    private sealed class StubLanguages : ILanguageProvider
    {
        public IReadOnlyList<LanguageInfo> Enabled() => [];
        public string DefaultCode() => "en";
        public bool IsEnabled(string code) => true;
        public void Invalidate() { }
    }

    private sealed class StubPermissions : IPermissionService
    {
        public bool CanRead(string c) => true;
        public bool CanWrite(string c) => true;
        public bool CanDelete(string c) => true;
        public IReadOnlyCollection<string> ReadableFields(string c, IEnumerable<string> all) =>
            all.ToList();
    }

    private sealed class StubFilterResolver : IRelationFilterResolver
    {
        public Task<FilterNode?> RewriteAsync(string c, FilterNode? f, string? locale, CancellationToken ct) =>
            Task.FromResult(f);
    }

    // ── test ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task DeleteAsync_restrict_with_guid_pk_returns_conflict_not_bad_request()
    {
        const string targetColl = "widget";
        const string sourceColl = "gadget";
        const string fk         = "widgetId";

        var guidId = Guid.CreateVersion7();

        var descriptor = new EntityDescriptor(
            typeof(GuidEntity),
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["id"] = "Id" },
            "Id");

        var graph    = new StubGraph(targetColl, sourceColl, fk);
        var registry = new StubRegistry(targetColl, descriptor);
        var repo     = new StubRepo();
        var meta     = new StubMeta(targetColl);
        var svc      = new ItemService(
            repo, meta, registry, new StubPermissions(),
            graph, new StubExpander(), new StubM2M(),
            new StubFilterResolver(), new StubLanguages(),
            new StruoQueryOptions(), new GanssHtmlSanitizer());

        // Act: delete a Guid-keyed row that is still referenced — must throw RelationConflictException
        // (conflict / 409), NOT a QueryException from a failed Convert.ChangeType (spurious 400).
        var act = async () => await svc.DeleteAsync(targetColl, guidId.ToString());
        await act.Should().ThrowAsync<RelationConflictException>();
    }
}
