// tests/Struo.Tests/Query/M2MRevertDedupTests.cs
using System.Text.Json;
using SqlSugar;
using Struo.Application.Configuration;
using Struo.Application.Localization;
using Struo.Application.Query;
using Struo.Application.Revisions;
using Struo.Domain.Auditing;
using Struo.Domain.Metadata.Attributes;
using Struo.Domain.Metadata.Enums;
using Struo.Domain.Query;
using Struo.Infrastructure.Localization;
using Struo.Infrastructure.Metadata;
using Struo.Infrastructure.Persistence;
using Struo.Infrastructure.Query;
using Struo.Infrastructure.Revisions;
using Struo.Infrastructure.Security;
using Struo.Sample.Blog;
using Struo.Tests.Support;
using Xunit;

namespace Struo.Tests.Query;

// ── Test-local entities ─────────────────────────────────────────────
//
// The sample Blog M2M target (Tag) is NOT ISoftDeletable, so it cannot be trashed — and the
// "revert tolerates a trashed M2M target" case requires exactly that. Following the precedent set by
// PurgeIntegrityTests (which declares its own [CmsCollection] fixtures for scenarios the sample domain
// can't express), this file declares a revisioned parent (Doc) with an M2M relation to a
// soft-deletable target (Label) plus the junction (DocLabel).

/// <summary>A soft-deletable M2M target so a target row can be trashed and a revert can then be
/// asked to reference it (snapshot was captured while it was still live).</summary>
[SugarTable("test_labels")]
[CmsCollection("Label")]
internal sealed class Label : AuditableEntity, ISoftDeletable
{
    [SugarColumn(IsPrimaryKey = true)] public override Guid Id { get; set; }
    public DateTime? DeletedAt { get; set; }
    public Guid? DeletedBy { get; set; }

    [CmsField(Label = "Name", Interface = FieldInterface.Text, Required = true)]
    public string Name { get; set; } = "";
}

/// <summary>A revisioned parent with an M2M relation to <see cref="Label"/> — revisioned so RevertAsync
/// can re-apply a past snapshot that still references a now-trashed label.</summary>
[SugarTable("test_docs")]
[CmsCollection("Doc", Revisions = true)]
internal sealed class Doc : AuditableEntity
{
    [SugarColumn(IsPrimaryKey = true)] public override Guid Id { get; set; }

    [CmsField(Label = "Title", Interface = FieldInterface.Text)]
    public string Title { get; set; } = "";

    [Navigate(typeof(DocLabel), nameof(DocLabel.DocId), nameof(DocLabel.LabelId))]
    [CmsRelation(Interface = RelationInterface.TagSelect, DisplayTemplate = "{Name}")]
    [SugarColumn(IsIgnore = true)]
    public List<Label> Labels { get; set; } = [];
}

[SugarTable("test_doc_labels")]
internal sealed class DocLabel
{
    [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }
    public Guid DocId { get; set; }
    public Guid LabelId { get; set; }
}

/// <summary>Wires a real <see cref="ItemService"/> over the Doc/Label/DocLabel fixtures (plus the sample
/// types the metadata/registry expects), mirroring <c>PurgeIntegrityHarness</c>.</summary>
internal sealed class M2MRevertHarness : IDisposable
{
    private readonly SqliteTestDatabase _file = new();

    public IItemRepository Repository { get; }
    public ItemService Service { get; }
    public IRevisionStore RevisionStore { get; }

    private M2MRevertHarness()
    {
        var currentUser = new TestCurrentUserAccessor(Guid.Empty);
        var db = SqlSugarClientFactory.Create(
            new DatabaseOptions { DbType = StruoDbType.Sqlite, ConnectionString = _file.ConnectionString },
            currentUser);
        db.CodeFirst.InitTables<Language>();
        db.CodeFirst.InitTables<Revision>();
        db.CodeFirst.InitTables<Doc>();
        db.CodeFirst.InitTables<Label>();
        db.CodeFirst.InitTables<DocLabel>();
        LanguageSeeder.SeedAsync(db).GetAwaiter().GetResult();

        var types = new[] { typeof(Doc), typeof(Label), typeof(DocLabel) };
        var collections = MetadataScanner.ScanTypes(types);
        var provider = new CachedMetadataProvider(collections);
        var registry = new EntityRegistry(MetadataScanner.ScanDescriptors(types));
        var collectionTypes = new Dictionary<string, Type>(StringComparer.OrdinalIgnoreCase)
        {
            ["doc"]   = typeof(Doc),
            ["label"] = typeof(Label),
        };
        var graph = new RelationshipGraph(collections, collectionTypes);
        var repo = new SqlSugarItemRepository(db, registry, graph, provider, new StruoQueryOptions());
        var resolver = new RelationFilterResolver(repo, graph, provider, registry, new StruoQueryOptions());
        var expander = new RelationExpander(repo, graph, resolver, new StruoQueryOptions());
        var languages = new LanguageProvider(db);
        var realStore = new SqlSugarRevisionStore(db, currentUser);
        var snapshotBuilder = new RevisionSnapshotBuilder(repo, provider, registry, graph);

        Repository = repo;
        RevisionStore = realStore;
        Service = new ItemService(repo, provider, registry, new AllowAllPermissionService(),
            graph, expander, graph, resolver, languages, new StruoQueryOptions(), new GanssHtmlSanitizer(),
            currentUser, realStore, snapshotBuilder);
    }

    public static M2MRevertHarness Create() => new();

    public void Dispose() => _file.Dispose();

    private static JsonElement BodyOf(object obj)
    {
        using var doc = JsonDocument.Parse(JsonSerializer.Serialize(obj));
        return doc.RootElement.Clone();
    }

    public async Task<string> CreateLabelAsync(string name)
    {
        var created = await Service.CreateAsync("label", BodyOf(new { name }));
        return created["id"]!.ToString()!;
    }

    public async Task<string> CreateDocAsync(Guid[]? labels = null)
    {
        var body = new Dictionary<string, object?> { ["title"] = "D" };
        if (labels is not null) body["labels"] = labels;
        var created = await Service.CreateAsync("doc", BodyOf(body));
        return created["id"]!.ToString()!;
    }

    public Task UpdateDocLabelsAsync(string docId, Guid[] labels) =>
        Service.UpdateAsync("doc", docId, BodyOf(new { labels }));

    public Task<IReadOnlyList<object>> DocLabelsByDocAsync(Guid docId) =>
        Repository.QueryEntityWhereInAsync(typeof(DocLabel), "DocId", [docId]);
}

/// <summary>
/// (1) M2M target-id arrays are de-duplicated before validation and junction sync
/// (<c>labels:[l1,l1]</c> == <c>labels:[l1]</c>); (2) a REVERT tolerates a snapshot that references a
/// now-trashed target (it was legal when captured), while a NORMAL write still rejects a trashed target
/// (asymmetry preserved). Real SQLite-backed <see cref="ItemService"/>, no stubs.
/// </summary>
public sealed class M2MRevertDedupTests
{
    // ── (a) duplicate ids in the body collapse to a single junction row (no false "do not exist") ──

    [Fact]
    public async Task Update_with_duplicate_m2m_ids_dedups_to_one_junction_row()
    {
        using var h = M2MRevertHarness.Create();
        var labelId = Guid.Parse(await h.CreateLabelAsync("L1"));
        var docId = Guid.Parse(await h.CreateDocAsync());

        await h.UpdateDocLabelsAsync(docId.ToString(), [labelId, labelId]);

        var junctions = await h.DocLabelsByDocAsync(docId);
        Assert.Single(junctions);
    }

    // ── (b) revert to a snapshot that references a now-trashed label succeeds and restores the row ──

    [Fact]
    public async Task Revert_tolerates_a_trashed_m2m_target()
    {
        using var h = M2MRevertHarness.Create();
        var labelId = Guid.Parse(await h.CreateLabelAsync("L1"));
        var docId = Guid.Parse(await h.CreateDocAsync(labels: [labelId])); // rev "create", snapshot has [l1]

        var createRev = (await h.RevisionStore.ListAsync("doc", docId.ToString()))
            .Single(r => r.Operation == "create");

        // Trash the label AFTER the snapshot was captured.
        await h.Service.DeleteAsync("label", labelId.ToString(), purge: false);
        Assert.Null(await h.Repository.GetByIdAsync("label", labelId.ToString(), DeletedFilter.Exclude));

        // Revert must NOT 400 on the now-trashed target (snapshot was legal when captured).
        var reverted = await h.Service.RevertAsync("doc", docId.ToString(), createRev.RevisionNumber);

        Assert.NotNull(reverted);
        var junctions = await h.DocLabelsByDocAsync(docId);
        Assert.Single(junctions);
    }

    // ── (c) a NORMAL update referencing a trashed label is still rejected (asymmetry held) ─────────

    [Fact]
    public async Task Normal_update_referencing_a_trashed_m2m_target_still_rejected()
    {
        using var h = M2MRevertHarness.Create();
        var labelId = Guid.Parse(await h.CreateLabelAsync("L1"));
        var docId = Guid.Parse(await h.CreateDocAsync());
        await h.Service.DeleteAsync("label", labelId.ToString(), purge: false); // trash the label

        await Assert.ThrowsAsync<QueryException>(
            () => h.UpdateDocLabelsAsync(docId.ToString(), [labelId]));
    }
}
