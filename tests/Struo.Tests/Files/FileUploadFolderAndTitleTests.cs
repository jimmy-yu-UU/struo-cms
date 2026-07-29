// tests/Struo.Tests/Files/FileUploadFolderAndTitleTests.cs
using AwesomeAssertions;
using SqlSugar;
using Struo.Application.Configuration;
using Struo.Application.Files;
using Struo.Application.Localization;
using Struo.Application.Query;
using Struo.Domain.Localization;
using Struo.Domain.Query;
using Struo.Infrastructure.Files;
using Struo.Infrastructure.Metadata;
using Struo.Infrastructure.Persistence;
using Struo.Infrastructure.Query;
using Struo.Tests.Support;
using Xunit;
using File = Struo.Infrastructure.Files.File;
using FileTranslation = Struo.Infrastructure.Files.FileTranslation;

namespace Struo.Tests.Files;

// Upload seeds a default-locale Title from the filename (extension stripped); upload
// accepts an optional folderId, validated against MediaFolder existence.
public class FileUploadFolderAndTitleTests : IDisposable
{
    private sealed class NoopStorage : IFileStorage
    {
        public Task SaveAsync(string key, Stream content, string contentType, CancellationToken ct = default) => Task.CompletedTask;
        public Task<Stream> OpenReadAsync(string key, CancellationToken ct = default) => Task.FromResult<Stream>(new MemoryStream());
        public Task DeleteAsync(string key, CancellationToken ct = default) => Task.CompletedTask;
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

    private static readonly Guid Tester = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");

    private readonly SqliteTestDatabase _file = new();
    private readonly ISqlSugarClient _db;
    private readonly ILanguageProvider _languages = new StubLanguages();
    private readonly FileService _svc;

    public FileUploadFolderAndTitleTests()
    {
        _db = SqlSugarClientFactory.Create(
            new DatabaseOptions { DbType = StruoDbType.Sqlite, ConnectionString = _file.ConnectionString },
            new TestCurrentUserAccessor(Tester));
        _db.CodeFirst.InitTables<File>();
        _db.CodeFirst.InitTables<FileTranslation>();
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
        var repo = new SqlSugarItemRepository(_db, registry, graph, provider, new StruoQueryOptions());
        _svc = new FileService(
            _db, new NoopStorage(), new NoopImages(), new FileStorageOptions(), repo, _languages,
            new TestCurrentUserAccessor(Tester));
    }

    public void Dispose() => _file.Dispose();

    private async Task<Guid> InsertFolderAsync(string name = "Folder A")
    {
        var id = Guid.NewGuid();
        await _db.Insertable(new MediaFolder
        {
            Id = id, Name = name, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow,
        }).ExecuteCommandAsync();
        return id;
    }

    [Fact]
    public async Task Upload_seeds_default_locale_title_without_extension()
    {
        var file = await _svc.UploadAsync(
            new MemoryStream([0xFF, 0xD8, 0xFF]), "photo-01.jpg", "image/jpeg", length: 3);

        var rows = await _db.Queryable<FileTranslation>().Where(t => t.FileId == file.Id).ToListAsync();
        rows.Should().HaveCount(1);
        rows[0].Locale.Should().Be(_languages.DefaultCode());
        rows[0].Title.Should().Be("photo-01");
        rows[0].Alt.Should().BeNull();
    }

    [Fact]
    public async Task Upload_dotfile_falls_back_to_full_name()
    {
        var file = await _svc.UploadAsync(
            new MemoryStream([1]), ".gitignore", "text/plain", length: 1);

        var row = await _db.Queryable<FileTranslation>().Where(t => t.FileId == file.Id).FirstAsync();
        row!.Title.Should().Be(".gitignore");
    }

    [Fact]
    public async Task Upload_with_valid_folderId_sets_folder()
    {
        var folderId = await InsertFolderAsync();

        var file = await _svc.UploadAsync(
            new MemoryStream([1]), "x.bin", "application/octet-stream", length: 1, folderId: folderId);

        file.FolderId.Should().Be(folderId);
        var stored = await _db.Queryable<File>().In(file.Id).FirstAsync();
        stored!.FolderId.Should().Be(folderId);
    }

    [Fact]
    public async Task Upload_with_unknown_folderId_throws_QueryException()
    {
        var unknown = Guid.NewGuid();

        var act = async () => await _svc.UploadAsync(
            new MemoryStream([1]), "x.bin", "application/octet-stream", length: 1, folderId: unknown);

        await act.Should().ThrowAsync<QueryException>();
        (await _db.Queryable<File>().CountAsync()).Should().Be(0);
        (await _db.Queryable<FileTranslation>().CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task Upload_without_folderId_leaves_folder_null()
    {
        var file = await _svc.UploadAsync(
            new MemoryStream([1]), "x.bin", "application/octet-stream", length: 1);

        file.FolderId.Should().BeNull();
        var row = await _db.Queryable<FileTranslation>().Where(t => t.FileId == file.Id).FirstAsync();
        row.Should().NotBeNull();
    }

    [Fact]
    public async Task Upload_title_longer_than_255_is_truncated()
    {
        var longName = new string('a', 300) + ".bin";

        var file = await _svc.UploadAsync(
            new MemoryStream([1]), longName, "application/octet-stream", length: 1);

        var row = await _db.Queryable<FileTranslation>().Where(t => t.FileId == file.Id).FirstAsync();
        row!.Title!.Length.Should().Be(255);
    }
}
