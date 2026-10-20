using AwesomeAssertions;
using SqlSugar;
using Struo.Application.Changes;
using Struo.Application.Configuration;
using Struo.Application.Files;
using Struo.Application.Localization;
using Struo.Application.Query;
using Struo.Domain.Localization;
using Struo.Domain.Metadata.Attributes;
using Struo.Domain.Metadata.Enums;
using Struo.Domain.Query;
using Struo.Infrastructure.Changes;
using Struo.Infrastructure.Files;
using Struo.Infrastructure.Metadata;
using Struo.Infrastructure.Persistence;
using Struo.Infrastructure.Query;
using Struo.Infrastructure.Revisions;
using Struo.Infrastructure.Security;
using Struo.Infrastructure.Settings;
using Struo.Tests.Support;
using Xunit;
using File = Struo.Infrastructure.Files.File;

namespace Struo.Tests.Files;

[SugarTable("test_fileref_docs")]
[CmsCollection("FileRefDoc")]
internal sealed class FileRefDoc
{
    [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }

    [CmsField(Label = "Name", Interface = FieldInterface.Text)]
    public string Name { get; set; } = "";

    [SugarColumn(IsNullable = true)]
    public Guid? CoverId { get; set; }

    [Navigate(NavigateType.OneToOne, nameof(CoverId))]
    [CmsRelation(Interface = RelationInterface.Dropdown, OnDelete = OnDelete.SetNull)]
    [SugarColumn(IsIgnore = true)]
    public File? Cover { get; set; }

    [Navigate(typeof(FileRefDocAttachment), nameof(FileRefDocAttachment.DocId), nameof(FileRefDocAttachment.FileId))]
    [CmsRelation(Interface = RelationInterface.TagSelect)]
    [SugarColumn(IsIgnore = true)]
    public List<File> Attachments { get; set; } = [];
}

[SugarTable("test_fileref_doc_attachments")]
internal sealed class FileRefDocAttachment
{
    [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }
    public Guid DocId { get; set; }
    public Guid FileId { get; set; }
}

[SugarTable("test_fileref_restricts")]
[CmsCollection("FileRestrictDoc")]
internal sealed class FileRestrictDoc
{
    [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }

    [CmsField(Label = "Name", Interface = FieldInterface.Text)]
    public string Name { get; set; } = "";

    public Guid FileId { get; set; }

    [Navigate(NavigateType.OneToOne, nameof(FileId))]
    [CmsRelation(Interface = RelationInterface.Dropdown, OnDelete = OnDelete.Restrict)]
    [SugarColumn(IsIgnore = true)]
    public File? Target { get; set; }
}

// A file purge removes what every collection's purge removes (SetNull of referencing keys, junction
// rows, Restrict guard) plus the file-specific cleanup (translations, logo reference, blob), whether it
// enters through the generic items service or the file service the media library uses.
public sealed class FileReferenceCleanupTests : IDisposable
{
    private sealed class RecordingStorage : IFileStorage
    {
        public readonly List<string> Deleted = [];
        public Task SaveAsync(string key, Stream content, string contentType, CancellationToken ct = default) => Task.CompletedTask;
        public Task<Stream> OpenReadAsync(string key, CancellationToken ct = default) => Task.FromResult<Stream>(new MemoryStream());
        public Task DeleteAsync(string key, CancellationToken ct = default) { Deleted.Add(key); return Task.CompletedTask; }
        public bool SupportsPresignedUrls => false;
        public Task<string?> GetPresignedUrlAsync(string key, TimeSpan ttl, CancellationToken ct = default) => Task.FromResult<string?>(null);
    }

    private sealed class NoopImages : IImageDimensionReader
    {
        public (int Width, int Height)? TryRead(Stream seekable, string contentType) => null;
    }

    private sealed class StubLanguages : ILanguageProvider
    {
        public Task<IReadOnlyList<LanguageInfo>> GetEnabledAsync() => Task.FromResult<IReadOnlyList<LanguageInfo>>([]);
        public Task<string> DefaultCodeAsync() => Task.FromResult("en");
        public Task<bool> IsEnabledAsync(string code) => Task.FromResult(true);
        public Task<string?> CanonicalizeAsync(string code) => Task.FromResult<string?>(code);
        public void Invalidate() { }
    }

    private static readonly Guid Tester = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");

    private readonly ITestDatabase _file = TestBackend.CreateUnitDatabase();
    private readonly ISqlSugarClient _db;
    private readonly RecordingStorage _storage = new();
    private readonly RecordingItemChangeListener _listener = new();
    private readonly FileService _files;
    private readonly ItemService _items;

    public FileReferenceCleanupTests()
    {
        _db = SqlSugarClientFactory.Create(_file.Options(), new TestCurrentUserAccessor(Tester));
        _db.CodeFirst.InitTables<File>();
        _db.CodeFirst.InitTables<FileTranslation>();
        _db.CodeFirst.InitTables<SiteSettings>();
        _db.CodeFirst.InitTables<MediaFolder>();
        _db.CodeFirst.InitTables<FileRefDoc>();
        _db.CodeFirst.InitTables<FileRefDocAttachment>();
        _db.CodeFirst.InitTables<FileRestrictDoc>();
        _db.CodeFirst.InitTables<Revision>();

        Type[] types = [typeof(File), typeof(MediaFolder), typeof(FileRefDoc), typeof(FileRestrictDoc)];
        var collections = MetadataScanner.ScanTypes(types);
        var provider = new CachedMetadataProvider(collections);
        var registry = new EntityRegistry(MetadataScanner.ScanDescriptors(types));
        var collectionTypes = new Dictionary<string, Type>(StringComparer.OrdinalIgnoreCase)
        {
            ["file"] = typeof(File),
            ["mediafolder"] = typeof(MediaFolder),
            ["filerefdoc"] = typeof(FileRefDoc),
            ["filerestrictdoc"] = typeof(FileRestrictDoc),
        };
        var graph = new RelationshipGraph(collections, collectionTypes);
        var options = new StruoQueryOptions();
        var repo = new SqlSugarItemRepository(_db, registry, graph, provider, options);
        var currentUser = new TestCurrentUserAccessor(Tester);
        var revisions = new SqlSugarRevisionStore(_db, currentUser);
        var pipeline = new ItemPurgePipeline(repo, provider, registry, graph, graph, revisions);

        _files = new FileService(
            _db, new FileStorageServices(_storage, new NoopImages(), new FileStorageOptions()), new FileServiceCollaborators(repo, new StubLanguages(), currentUser),
            notifier: new ItemChangeNotifier([_listener], new ListLogger<ItemChangeNotifier>()), references: pipeline);
        _items = new ItemService(repo, provider, registry, new AllowAllPermissionService(),
            graph, new RelationExpander(repo, graph, options), graph, new StubLanguages(), options,
            new GanssHtmlSanitizer(), currentUser, revisions,
            new RevisionSnapshotBuilder(repo, provider, registry, graph), new NoopUserSessionRevocationService(),
            fileLifecycle: _files);
    }

    public void Dispose() => _file.Dispose();

    private sealed record Seeded(Guid FileId, string Key, Guid DocId);

    private async Task<Seeded> SeedAsync()
    {
        var fileId = Guid.NewGuid();
        var key = "k-" + fileId.ToString("N");
        await _db.Insertable(new File
        {
            Id = fileId, StorageKey = key, FileName = "a.png", ContentType = "image/png", Size = 1, Status = "published",
        }).ExecuteCommandAsync();
        await _db.Insertable(new FileTranslation { FileId = fileId, Locale = "en", Title = "A" }).ExecuteCommandAsync();
        await _db.Deleteable<SiteSettings>().Where(s => s.Id == SiteSettings.SingletonId).ExecuteCommandAsync();
        await _db.Insertable(new SiteSettings
        {
            Id = SiteSettings.SingletonId, BrandName = "B", LogoFileId = fileId, UpdatedAt = DateTime.UtcNow,
        }).ExecuteCommandAsync();
        var docId = Guid.NewGuid();
        await _db.Insertable(new FileRefDoc { Id = docId, Name = "d", CoverId = fileId }).ExecuteCommandAsync();
        await _db.Insertable(new FileRefDocAttachment { Id = Guid.NewGuid(), DocId = docId, FileId = fileId })
            .ExecuteCommandAsync();
        return new Seeded(fileId, key, docId);
    }

    private async Task AssertFullyCleanedAsync(Seeded s)
    {
        (await _db.Queryable<File>().ClearFilter<Struo.Domain.Auditing.ISoftDeletable>()
            .Where(f => f.Id == s.FileId).AnyAsync()).Should().BeFalse();
        (await _db.Queryable<FileTranslation>().Where(t => t.FileId == s.FileId).AnyAsync()).Should().BeFalse();
        (await _db.Queryable<FileRefDoc>().In(s.DocId).FirstAsync())!.CoverId.Should().BeNull();
        (await _db.Queryable<FileRefDocAttachment>().Where(a => a.FileId == s.FileId).AnyAsync()).Should().BeFalse();
        (await _db.Queryable<SiteSettings>().FirstAsync())!.LogoFileId.Should().BeNull();
        _storage.Deleted.Should().Contain(s.Key);
    }

    [Fact]
    public async Task Media_library_purge_clears_references_and_file_artifacts()
    {
        var s = await SeedAsync();

        (await _files.DeleteAsync(s.FileId)).Should().BeTrue();

        await AssertFullyCleanedAsync(s);
    }

    [Fact]
    public async Task Generic_purge_clears_references_and_file_artifacts()
    {
        var s = await SeedAsync();

        (await _items.DeleteAsync("file", s.FileId.ToString(), purge: true)).Should().BeTrue();

        await AssertFullyCleanedAsync(s);
    }

    [Fact]
    public async Task Restrict_reference_blocks_purge_and_trash_on_both_paths()
    {
        var s = await SeedAsync();
        await _db.Insertable(new FileRestrictDoc { Id = Guid.NewGuid(), Name = "r", FileId = s.FileId }).ExecuteCommandAsync();

        var viaFiles = async () => await _files.DeleteAsync(s.FileId);
        var viaItems = async () => await _items.DeleteAsync("file", s.FileId.ToString(), purge: true);
        var trashFiles = async () => await _files.TrashAsync(s.FileId);
        var trashItems = async () => await _items.DeleteAsync("file", s.FileId.ToString());

        await viaFiles.Should().ThrowAsync<RelationConflictException>();
        await viaItems.Should().ThrowAsync<RelationConflictException>();
        await trashFiles.Should().ThrowAsync<RelationConflictException>();
        await trashItems.Should().ThrowAsync<RelationConflictException>();
        (await _db.Queryable<File>().In(s.FileId).AnyAsync()).Should().BeTrue();
        (await _db.Queryable<FileRefDoc>().In(s.DocId).FirstAsync())!.CoverId.Should().Be(s.FileId);
        _storage.Deleted.Should().BeEmpty();
    }

    [Fact]
    public async Task Purge_announces_the_file_as_purged_and_each_affected_row_as_updated()
    {
        var s = await SeedAsync();

        await _files.DeleteAsync(s.FileId);

        var all = _listener.All.ToList();
        all.Should().Contain(c => c.Kind == ItemChangeKind.Purged && c.Collection == "file" && c.Id == s.FileId.ToString());
        all.Should().Contain(c => c.Kind == ItemChangeKind.Updated
            && string.Equals(c.Collection, "FileRefDoc", StringComparison.OrdinalIgnoreCase) && c.Id == s.DocId.ToString());
    }

    [Fact]
    public async Task Purge_of_an_unknown_file_is_false_and_announces_nothing()
    {
        (await _files.DeleteAsync(Guid.NewGuid())).Should().BeFalse();

        _listener.All.Should().BeEmpty();
        _storage.Deleted.Should().BeEmpty();
    }
}
