// tests/Struo.Tests/Query/SelfReferenceCycleGuardTests.cs
using Struo.Tests.Support.Seeding;
using System.Text.Json;
using AwesomeAssertions;
using Struo.Application.Configuration;
using Struo.Application.Metadata;
using Struo.Application.Query;
using Struo.Domain.Query;
using Struo.Infrastructure.Localization;
using Struo.Infrastructure.Metadata;
using Struo.Infrastructure.Persistence;
using Struo.Infrastructure.Query;
using Struo.Infrastructure.Revisions;
using Struo.Infrastructure.Security;
using Struo.Tests.Fixtures;
using Struo.Tests.Support;
using Xunit;
using File = Struo.Infrastructure.Files.File;
using FileTranslation = Struo.Infrastructure.Files.FileTranslation;
using MediaFolder = Struo.Infrastructure.Files.MediaFolder;

namespace Struo.Tests.Query;

/// <summary>
/// Self-reference cycle guard: <see cref="SelfReferenceCycleGuard"/> rejects an
/// UpdateAsync that would set a self-referencing ManyToOne FK (parentId) to the item itself or one of
/// its descendants. Metadata-driven via <c>RelationMetadata.SelfReferencing</c> — covers both the core
/// MediaFolder tree and the fixture FxCategory tree, proving the guard is not special-cased to
/// mediafolder. Fixture mirrors ItemServiceTests's combined FxArticle+FxCategory+FxTag+File+MediaFolder
/// registration, extended with the File/MediaFolder InitTables from MediaFolderTests so mediafolder
/// rows can actually be created/updated here.
/// </summary>
public sealed class SelfReferenceCycleGuardTests : IDisposable
{
    private readonly ITestDatabase _file = TestBackend.CreateUnitDatabase();
    private readonly ItemService _svc;

    public SelfReferenceCycleGuardTests()
    {
        var db = SqlSugarClientFactory.Create(
            _file.Options(),
            new TestCurrentUserAccessor(Guid.Empty));
        db.CodeFirst.InitTables<FxArticle>();
        db.CodeFirst.InitTables<FxArticleTranslation>();
        db.CodeFirst.InitTables<Language>();
        db.CodeFirst.InitTables<FxTag>();
        db.CodeFirst.InitTables<FxArticleTag>();
        db.CodeFirst.InitTables<FxCategory>();
        db.CodeFirst.InitTables<MediaFolder>();
        db.CodeFirst.InitTables<File>();
        db.CodeFirst.InitTables<FileTranslation>();
        db.CodeFirst.InitTables<Revision>();
        LanguageSeeder.SeedAsync(db, TestLocalization.Default).GetAwaiter().GetResult();

        var types = new[]
        {
            typeof(FxArticle), typeof(FxCategory), typeof(FxTag), typeof(File), typeof(MediaFolder),
        };
        var collections = MetadataScanner.ScanTypes(types);
        var provider = new CachedMetadataProvider(collections);
        var registry = new EntityRegistry(MetadataScanner.ScanDescriptors(types));
        var collectionTypes = new Dictionary<string, Type>(StringComparer.OrdinalIgnoreCase)
        {
            ["fxArticle"] = typeof(FxArticle),
            ["fxCategory"] = typeof(FxCategory),
            ["fxTag"] = typeof(FxTag),
            ["file"] = typeof(File),
            ["mediafolder"] = typeof(MediaFolder),
        };
        var graph = new RelationshipGraph(collections, collectionTypes);
        var repo = new SqlSugarItemRepository(db, registry, graph, provider, new StruoQueryOptions());
        var expander = new RelationExpander(repo, graph, new StruoQueryOptions());
        var languages = new LanguageProvider(db);
        var currentUser = new TestCurrentUserAccessor(Guid.Empty);
        var revisionStore = new SqlSugarRevisionStore(db, currentUser);
        var snapshotBuilder = new RevisionSnapshotBuilder(repo, provider, registry, graph);

        _svc = TestItemService.Create(
            repo,
            provider,
            registry,
            graph,
            expander,
            graph,
            languages,
            currentUser,
            revisionStore,
            snapshotBuilder);
    }

    public void Dispose() => _file.Dispose();

    private static JsonElement BodyOf(object obj)
    {
        using var doc = JsonDocument.Parse(JsonSerializer.Serialize(obj));
        return doc.RootElement.Clone();
    }

    [Fact]
    public async Task Setting_parent_to_self_is_rejected()
    {
        var a = await _svc.CreateAsync("mediafolder", BodyOf(new { name = "A" }));
        var aId = ((Guid)a["id"]!).ToString();

        // name is Required+non-translatable, so the merge-update body must still carry it (the
        // deserializer validates the incoming payload's required fields independently of the
        // existing-row overlay) — unrelated to the guard under test.
        var ex = await Assert.ThrowsAsync<QueryException>(
            () => _svc.UpdateAsync("mediafolder", aId, BodyOf(new { name = "A", parentId = aId })));
        ex.Message.Should().Contain("cycle");
    }

    [Fact]
    public async Task Setting_parent_to_own_descendant_is_rejected()
    {
        var a = await _svc.CreateAsync("mediafolder", BodyOf(new { name = "A" }));
        var aId = (Guid)a["id"]!;
        var b = await _svc.CreateAsync("mediafolder", BodyOf(new { name = "B", parentId = aId }));
        var bId = (Guid)b["id"]!;
        var c = await _svc.CreateAsync("mediafolder", BodyOf(new { name = "C", parentId = bId }));
        var cId = (Guid)c["id"]!;

        // A ← B ← C: pointing A's parent at its own descendant C must be rejected.
        var ex = await Assert.ThrowsAsync<QueryException>(
            () => _svc.UpdateAsync("mediafolder", aId.ToString(), BodyOf(new { name = "A", parentId = cId })));
        ex.Message.Should().Contain("cycle");
    }

    [Fact]
    public async Task Reparenting_to_sibling_branch_succeeds()
    {
        var a = await _svc.CreateAsync("mediafolder", BodyOf(new { name = "A" }));
        var aId = (Guid)a["id"]!;
        var b = await _svc.CreateAsync("mediafolder", BodyOf(new { name = "B", parentId = aId }));
        var bId = (Guid)b["id"]!;
        var c = await _svc.CreateAsync("mediafolder", BodyOf(new { name = "C", parentId = aId }));
        var cId = (Guid)c["id"]!;

        var updated = await _svc.UpdateAsync(
            "mediafolder", cId.ToString(), BodyOf(new { name = "C", parentId = bId }));
        updated.Should().NotBeNull();

        // parentId is a relation FK (declared via [CmsRelation], not [CmsField]) so it is never
        // projected onto the update response dict — verify the persisted value via a query filter,
        // the same pattern MediaFolderTests uses for File.FolderId.
        var filter = new ComparisonFilter("parentId", QueryOperator.Eq, bId.ToString());
        var result = await _svc.QueryAsync("mediafolder", new QueryModel(null, filter, [], 0, 0, null));
        result.Data.Should().Contain(d => (Guid)d["id"]! == cId);
    }

    [Fact]
    public async Task Clearing_parent_succeeds()
    {
        var a = await _svc.CreateAsync("mediafolder", BodyOf(new { name = "A" }));
        var aId = (Guid)a["id"]!;
        var b = await _svc.CreateAsync("mediafolder", BodyOf(new { name = "B", parentId = aId }));
        var bId = (Guid)b["id"]!;

        var updated = await _svc.UpdateAsync(
            "mediafolder", bId.ToString(), BodyOf(new { name = "B", parentId = (Guid?)null }));
        updated.Should().NotBeNull();

        var filter = new ComparisonFilter("parentId", QueryOperator.Null, null);
        var result = await _svc.QueryAsync("mediafolder", new QueryModel(null, filter, [], 0, 0, null));
        result.Data.Should().Contain(d => (Guid)d["id"]! == bId);
    }

    [Fact]
    public async Task Category_fixture_collection_is_also_guarded()
    {
        // Proves the guard is metadata-driven (RelationMetadata.SelfReferencing), not hardcoded to
        // "mediafolder": the fixture FxCategory collection has its own self-referencing ParentId tree.
        var x = await _svc.CreateAsync("fxCategory", BodyOf(new { name = "X" }));
        var xId = ((Guid)x["id"]!).ToString();

        var ex = await Assert.ThrowsAsync<QueryException>(
            () => _svc.UpdateAsync("fxCategory", xId, BodyOf(new { name = "X", parentId = xId })));
        ex.Message.Should().Contain("cycle");
    }

    [Fact]
    public async Task Cycle_through_a_trashed_intermediate_node_is_still_rejected()
    {
        // FxCategory is ISoftDeletable. The guard reasons about FK topology, not row visibility, so a
        // trashed ancestor must still be walked — repro for the reviewer-found bug where the
        // ancestor-walk fetch defaulted to DeletedFilter.Exclude and silently treated a trashed
        // intermediate node as a dangling (non-existent) parent, letting a cycle through it escape
        // detection.
        var a = await _svc.CreateAsync("fxCategory", BodyOf(new { name = "A" }));
        var aId = (Guid)a["id"]!;
        var b = await _svc.CreateAsync("fxCategory", BodyOf(new { name = "B", parentId = aId }));
        var bId = (Guid)b["id"]!;
        var c = await _svc.CreateAsync("fxCategory", BodyOf(new { name = "C", parentId = bId }));
        var cId = (Guid)c["id"]!;

        // Trash B (soft delete) — B.parentId = A still persists on the trashed row.
        var trashed = await _svc.DeleteAsync("fxCategory", bId.ToString());
        trashed.Should().BeTrue();

        // A ← B(trashed) ← C. Repointing A's parent at C creates A→C→B→A: the walk must pass THROUGH
        // the trashed B to detect it, not stop there as if B did not exist.
        var ex = await Assert.ThrowsAsync<QueryException>(
            () => _svc.UpdateAsync("fxCategory", aId.ToString(), BodyOf(new { name = "A", parentId = cId })));
        ex.Message.Should().Contain("cycle");
    }
}
