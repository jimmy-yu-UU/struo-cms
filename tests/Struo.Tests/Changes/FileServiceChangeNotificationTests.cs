// tests/Struo.Tests/Changes/FileServiceChangeNotificationTests.cs
using AwesomeAssertions;
using SqlSugar;
using Struo.Application.Abstractions;
using Struo.Application.Changes;
using Struo.Application.Configuration;
using Struo.Application.Files;
using Struo.Application.Localization;
using Struo.Application.Query;
using Struo.Domain.Auditing;
using Struo.Domain.Localization;
using Struo.Infrastructure.Changes;
using Struo.Infrastructure.Files;
using Struo.Infrastructure.Metadata;
using Struo.Infrastructure.Persistence;
using Struo.Infrastructure.Query;
using Struo.Infrastructure.Settings;
using Struo.Tests.Support;
using Xunit;
using File = Struo.Infrastructure.Files.File;
using FileTranslation = Struo.Infrastructure.Files.FileTranslation;

namespace Struo.Tests.Changes;

// U5b: FileService's four write paths (upload/trash/restore/purge) bypass ItemService and must
// raise their own post-commit item changes. Harness copied from FileServiceSoftDeleteTests.
public sealed class FileServiceChangeNotificationTests : IDisposable
{
    private sealed class CountingStorage : IFileStorage
    {
        public int Deletes { get; private set; }
        public Task SaveAsync(string key, Stream content, string contentType, CancellationToken ct = default) => Task.CompletedTask;
        public Task<Stream> OpenReadAsync(string key, CancellationToken ct = default) => Task.FromResult<Stream>(new MemoryStream());
        public Task DeleteAsync(string key, CancellationToken ct = default) { Deletes++; return Task.CompletedTask; }
        public bool SupportsPresignedUrls => false;
        public Task<string?> GetPresignedUrlAsync(string key, TimeSpan ttl, CancellationToken ct = default) => Task.FromResult<string?>(null);
    }

    private sealed class NoopImages : IImageDimensionReader
    {
        public (int Width, int Height)? TryRead(Stream seekable, string contentType) => null;
    }

    private sealed class StubLanguages : ILanguageProvider
    {
        public IReadOnlyList<LanguageInfo> Enabled() => [];
        public string DefaultCode() => "en";
        public bool IsEnabled(string code) => true;
        public void Invalidate() { }
    }

    private static readonly Guid Tester = Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd");

    private readonly SqliteTestDatabase _file = new();
    private readonly ISqlSugarClient _db;
    private readonly CountingStorage _storage = new();
    private readonly IItemRepository _repo;
    private readonly RecordingItemChangeListener _listener = new();
    private readonly FileService _svc;

    public FileServiceChangeNotificationTests()
    {
        _db = SqlSugarClientFactory.Create(
            new DatabaseOptions { DbType = StruoDbType.Sqlite, ConnectionString = _file.ConnectionString },
            new TestCurrentUserAccessor(Tester));
        _db.CodeFirst.InitTables<File>();
        _db.CodeFirst.InitTables<FileTranslation>();
        _db.CodeFirst.InitTables<SiteSettings>();
        _db.CodeFirst.InitTables<MediaFolder>();

        var collections = MetadataScanner.ScanTypes([typeof(File), typeof(MediaFolder)]);
        var provider = new CachedMetadataProvider(collections);
        var registry = new EntityRegistry(MetadataScanner.ScanDescriptors([typeof(File), typeof(MediaFolder)]));
        var collectionTypes = new Dictionary<string, Type>(StringComparer.OrdinalIgnoreCase)
        {
            ["file"] = typeof(File),
            ["mediafolder"] = typeof(MediaFolder),
        };
        var graph = new RelationshipGraph(collections, collectionTypes);
        _repo = new SqlSugarItemRepository(_db, registry, graph, provider, new StruoQueryOptions());
        _svc = new FileService(_db, _storage, new NoopImages(), new FileStorageOptions(), _repo, new StubLanguages(),
            new TestCurrentUserAccessor(Tester),
            notifier: new ItemChangeNotifier([_listener], new ListLogger<ItemChangeNotifier>()));
    }

    public void Dispose() => _file.Dispose();

    private async Task<Guid> UploadAsync()
    {
        var f = await _svc.UploadAsync(new MemoryStream([1, 2, 3]), "a.bin", "application/octet-stream", 3);
        return f.Id;
    }

    private (string Collection, string Id, ItemChangeKind Kind) Only()
    {
        var c = _listener.Calls.Should().ContainSingle().Subject.Should().ContainSingle().Subject;
        return (c.Collection, c.Id, c.Kind);
    }

    [Fact]
    public async Task Upload_raises_Created()
    {
        var id = await UploadAsync();
        Only().Should().Be(("file", id.ToString(), ItemChangeKind.Created));
    }

    [Fact]
    public async Task Trash_raises_Trashed_once_and_restore_raises_Restored_once()
    {
        var id = await UploadAsync();
        _listener.Calls.Clear();
        (await _svc.TrashAsync(id)).Should().BeTrue();
        Only().Should().Be(("file", id.ToString(), ItemChangeKind.Trashed));
        _listener.Calls.Clear();
        (await _svc.TrashAsync(id)).Should().BeFalse();
        _listener.Calls.Should().BeEmpty();
        (await _svc.RestoreAsync(id)).Should().BeTrue();
        Only().Should().Be(("file", id.ToString(), ItemChangeKind.Restored));
        _listener.Calls.Clear();
        (await _svc.RestoreAsync(id)).Should().BeFalse();
        _listener.Calls.Should().BeEmpty();
    }

    [Fact]
    public async Task Purge_raises_Purged_and_an_unknown_id_raises_nothing()
    {
        var id = await UploadAsync();
        _listener.Calls.Clear();
        (await _svc.DeleteAsync(id)).Should().BeTrue();
        Only().Should().Be(("file", id.ToString(), ItemChangeKind.Purged));
        _listener.Calls.Clear();
        (await _svc.DeleteAsync(Guid.NewGuid())).Should().BeFalse();
        _listener.Calls.Should().BeEmpty();
    }

    [Fact]
    public async Task Without_a_notifier_the_service_behaves_as_before()
    {
        var plain = new FileService(_db, _storage, new NoopImages(), new FileStorageOptions(), _repo, new StubLanguages(),
            new TestCurrentUserAccessor(Tester));
        var f = await plain.UploadAsync(new MemoryStream([1]), "b.bin", "application/octet-stream", 1);
        (await plain.TrashAsync(f.Id)).Should().BeTrue();
        _listener.Calls.Should().BeEmpty();
    }
}
