// tests/Struo.Tests/Files/MediaFolderTests.cs
using System.Text.Json;
using AwesomeAssertions;
using Struo.Application.Configuration;
using Struo.Application.Metadata;
using Struo.Application.Query;
using Struo.Domain.Query;
using Struo.Infrastructure.Files;
using Struo.Infrastructure.Localization;
using Struo.Infrastructure.Metadata;
using Struo.Infrastructure.Persistence;
using Struo.Infrastructure.Query;
using Struo.Infrastructure.Revisions;
using Struo.Infrastructure.Security;
using Struo.Tests.Support;
using Xunit;
using File = Struo.Infrastructure.Files.File;
using FileTranslation = Struo.Infrastructure.Files.FileTranslation;

namespace Struo.Tests.Files;

/// <summary>
/// Media folders: MediaFolder core entity — self-referencing tree, Hidden
/// collection routed as <c>mediafolder</c>; File.FolderId nullable organisational FK. Fixture wiring
/// mirrors <c>ItemServiceTests</c>'s hand-wired-services pattern (SQLite in-memory + InitTables).
/// </summary>
public sealed class MediaFolderTests : IDisposable
{
    private readonly SqliteTestDatabase _file = new();
    private readonly ItemService _svc;
    private readonly IMetadataProvider _metadata;

    public MediaFolderTests()
    {
        var db = SqlSugarClientFactory.Create(
            new DatabaseOptions { DbType = StruoDbType.Sqlite, ConnectionString = _file.ConnectionString },
            new TestCurrentUserAccessor(Guid.Empty));
        db.CodeFirst.InitTables<MediaFolder>();
        db.CodeFirst.InitTables<File>();
        db.CodeFirst.InitTables<FileTranslation>();
        db.CodeFirst.InitTables<Language>();
        db.CodeFirst.InitTables<Revision>();
        LanguageSeeder.SeedAsync(db).GetAwaiter().GetResult();

        var types = new[] { typeof(MediaFolder), typeof(File) };
        var collections = MetadataScanner.ScanTypes(types);
        var provider = new CachedMetadataProvider(collections);
        var registry = new EntityRegistry(MetadataScanner.ScanDescriptors(types));
        var collectionTypes = new Dictionary<string, Type>(StringComparer.OrdinalIgnoreCase)
        {
            ["mediafolder"] = typeof(MediaFolder),
            ["file"] = typeof(File),
        };
        var graph = new RelationshipGraph(collections, collectionTypes);
        var repo = new SqlSugarItemRepository(db, registry, graph, provider, new StruoQueryOptions());
        var resolver = new RelationFilterResolver(repo, graph, provider, registry, new StruoQueryOptions());
        var expander = new RelationExpander(repo, graph, resolver, new StruoQueryOptions());
        var languages = new LanguageProvider(db);
        var currentUser = new TestCurrentUserAccessor(Guid.Empty);
        var revisionStore = new SqlSugarRevisionStore(db, currentUser);
        var snapshotBuilder = new RevisionSnapshotBuilder(repo, provider, registry, graph);

        _metadata = provider;
        _svc = new ItemService(repo, provider, registry, new AllowAllPermissionService(),
            graph, expander, graph, resolver, languages, new StruoQueryOptions(), new GanssHtmlSanitizer(),
            currentUser, revisionStore, snapshotBuilder);
    }

    public void Dispose() => _file.Dispose();

    private static JsonElement BodyOf(object obj)
    {
        using var doc = JsonDocument.Parse(JsonSerializer.Serialize(obj));
        return doc.RootElement.Clone();
    }

    [Fact]
    public void Mediafolder_collection_is_registered_and_hidden()
    {
        var meta = _metadata.GetCollection("mediafolder");

        meta.Should().NotBeNull();
        meta!.Hidden.Should().BeTrue();
        meta.Group.Should().Be("System");
    }

    [Fact]
    public async Task Create_list_update_folder_roundtrip()
    {
        var a = await _svc.CreateAsync("mediafolder", BodyOf(new { name = "A" }));
        var aId = (Guid)a["id"]!;

        var query = new QueryModel(null, null, [], 0, 0, null);
        var listed = await _svc.QueryAsync("mediafolder", query);
        listed.Data.Should().Contain(d => (Guid)d["id"]! == aId);

        var b = await _svc.CreateAsync("mediafolder", BodyOf(new { name = "B", parentId = aId }));
        var bId = (Guid)b["id"]!;

        var deep = new DeepSpec(
            new Dictionary<string, DeepRelationSpec>(StringComparer.OrdinalIgnoreCase)
            {
                ["parent"] = new DeepRelationSpec(null, null)
            });
        var fetchedB = await _svc.GetAsync("mediafolder", bId.ToString(), deep);
        fetchedB.Should().NotBeNull();
        var parent = (IReadOnlyDictionary<string, object?>)fetchedB!["parent"]!;
        parent["id"].Should().Be(aId);

        var updated = await _svc.UpdateAsync("mediafolder", bId.ToString(), BodyOf(new { name = "B2" }));
        updated.Should().NotBeNull();
        updated!["name"].Should().Be("B2");
    }

    [Fact]
    public async Task Files_filter_by_folderId_eq_and_null()
    {
        var a = await _svc.CreateAsync("mediafolder", BodyOf(new { name = "A" }));
        var aId = (Guid)a["id"]!;

        var db = SqlSugarClientFactory.Create(
            new DatabaseOptions { DbType = StruoDbType.Sqlite, ConnectionString = _file.ConnectionString },
            new TestCurrentUserAccessor(Guid.Empty));

        var inFolder = new File
        {
            Id = Guid.CreateVersion7(),
            StorageKey = "k1",
            FileName = "in-folder.txt",
            ContentType = "text/plain",
            Size = 1,
            Status = "draft",
            FolderId = aId,
        };
        var unfiled = new File
        {
            Id = Guid.CreateVersion7(),
            StorageKey = "k2",
            FileName = "unfiled.txt",
            ContentType = "text/plain",
            Size = 1,
            Status = "draft",
            FolderId = null,
        };
        await db.Insertable(inFolder).ExecuteCommandAsync();
        await db.Insertable(unfiled).ExecuteCommandAsync();

        var eqFilter = new ComparisonFilter("folderId", QueryOperator.Eq, aId.ToString());
        var eqResult = await _svc.QueryAsync("file", new QueryModel(null, eqFilter, [], 0, 0, null));
        eqResult.Data.Should().HaveCount(1);
        eqResult.Data[0]["id"].Should().Be(inFolder.Id);

        var nullFilter = new ComparisonFilter("folderId", QueryOperator.Null, null);
        var nullResult = await _svc.QueryAsync("file", new QueryModel(null, nullFilter, [], 0, 0, null));
        nullResult.Data.Should().HaveCount(1);
        nullResult.Data[0]["id"].Should().Be(unfiled.Id);
    }

    [Fact]
    public async Task Delete_folder_with_files_is_rejected_409()
    {
        var a = await _svc.CreateAsync("mediafolder", BodyOf(new { name = "A" }));
        var aId = (Guid)a["id"]!;

        var db = SqlSugarClientFactory.Create(
            new DatabaseOptions { DbType = StruoDbType.Sqlite, ConnectionString = _file.ConnectionString },
            new TestCurrentUserAccessor(Guid.Empty));
        var fileInFolder = new File
        {
            Id = Guid.CreateVersion7(),
            StorageKey = "k1",
            FileName = "in-folder.txt",
            ContentType = "text/plain",
            Size = 1,
            Status = "draft",
            FolderId = aId,
        };
        await db.Insertable(fileInFolder).ExecuteCommandAsync();

        await Assert.ThrowsAsync<RelationConflictException>(
            () => _svc.DeleteAsync("mediafolder", aId.ToString()));
    }

    [Fact]
    public async Task Delete_folder_with_subfolder_is_rejected_409()
    {
        var a = await _svc.CreateAsync("mediafolder", BodyOf(new { name = "A" }));
        var aId = (Guid)a["id"]!;
        await _svc.CreateAsync("mediafolder", BodyOf(new { name = "B", parentId = aId }));

        await Assert.ThrowsAsync<RelationConflictException>(
            () => _svc.DeleteAsync("mediafolder", aId.ToString()));
    }

    [Fact]
    public async Task Delete_empty_folder_succeeds()
    {
        var a = await _svc.CreateAsync("mediafolder", BodyOf(new { name = "A" }));
        var aId = (Guid)a["id"]!;

        var deleted = await _svc.DeleteAsync("mediafolder", aId.ToString());

        deleted.Should().BeTrue();
        var fetched = await _svc.GetAsync("mediafolder", aId.ToString());
        fetched.Should().BeNull();
    }
}
