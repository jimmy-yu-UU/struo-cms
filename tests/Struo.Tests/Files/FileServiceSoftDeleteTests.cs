// tests/Struo.Tests/Files/FileServiceSoftDeleteTests.cs
using AwesomeAssertions;
using SqlSugar;
using Struo.Application.Abstractions;
using Struo.Application.Configuration;
using Struo.Application.Files;
using Struo.Application.Localization;
using Struo.Application.Query;
using Struo.Domain.Auditing;
using Struo.Domain.Localization;
using Struo.Infrastructure.Files;
using Struo.Infrastructure.Metadata;
using Struo.Infrastructure.Persistence;
using Struo.Infrastructure.Query;
using Struo.Infrastructure.Settings;
using Struo.Tests.Support;
using Xunit;
using File = Struo.Infrastructure.Files.File;
using FileTranslation = Struo.Infrastructure.Files.FileTranslation;

namespace Struo.Tests.Files;

// File soft-delete — FileService.TrashAsync (default delete: stamps DeletedAt/DeletedBy,
// retains blob + FileTranslation rows, clears site_settings.logofileid when applicable) and
// FileService.RestoreAsync (clears DeletedAt/DeletedBy). FileService.DeleteAsync stays as-is and
// now serves as the "purge" operation (already hard-deletes row + translations + blob + logo ref).
public class FileServiceSoftDeleteTests : IDisposable
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
    private readonly FileService _svc;

    public FileServiceSoftDeleteTests()
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
        var repo = new SqlSugarItemRepository(_db, registry, graph, provider, new StruoQueryOptions());
        _svc = new FileService(
            _db, _storage, new NoopImages(), new FileStorageOptions(), repo, new StubLanguages(),
            new TestCurrentUserAccessor(Tester));
    }

    public void Dispose() => _file.Dispose();

    private async Task<Guid> InsertFileAsync()
    {
        var id = Guid.NewGuid();
        await _db.Insertable(new File
        {
            Id = id, StorageKey = "k", FileName = "logo.png", ContentType = "image/png",
            Size = 1, Status = "published",
        }).ExecuteCommandAsync();
        return id;
    }

    private Task InsertSiteSettingsAsync(Guid? logoFileId) =>
        _db.Insertable(new SiteSettings
        {
            Id = SiteSettings.SingletonId, BrandName = "Brand", LogoFileId = logoFileId, UpdatedAt = DateTime.UtcNow,
        }).ExecuteCommandAsync();

    [Fact]
    public async Task Trash_stamps_deletedat_and_retains_row_blob_and_translations()
    {
        var id = await InsertFileAsync();
        await _db.Insertable(new FileTranslation { FileId = id, Locale = "en", Title = "t" }).ExecuteCommandAsync();

        var ok = await _svc.TrashAsync(id);

        ok.Should().BeTrue();
        _storage.Deletes.Should().Be(0);                                   // blob retained
        var row = await _db.Queryable<File>().ClearFilter<ISoftDeletable>().In(id).FirstAsync();
        row!.DeletedAt.Should().NotBeNull();
        row.DeletedBy.Should().Be(Tester);
        (await _db.Queryable<FileTranslation>().Where(t => t.FileId == id).CountAsync()).Should().Be(1); // translations retained
    }

    [Fact]
    public async Task Trashed_file_is_excluded_from_default_reads()
    {
        var id = await InsertFileAsync();
        await _svc.TrashAsync(id);
        (await _svc.GetAsync(id)).Should().BeNull();                       // query filter hides it → 404 upstream
    }

    [Fact]
    public async Task Trash_of_current_logo_clears_logofileid()
    {
        var id = await InsertFileAsync();
        await InsertSiteSettingsAsync(id);
        await _svc.TrashAsync(id);
        var s = await _db.Queryable<SiteSettings>().In(SiteSettings.SingletonId).FirstAsync();
        s!.LogoFileId.Should().BeNull();
    }

    [Fact]
    public async Task Restore_clears_deletedat_and_makes_file_readable_again()
    {
        var id = await InsertFileAsync();
        await _svc.TrashAsync(id);

        var restored = await _svc.RestoreAsync(id);

        restored.Should().BeTrue();
        (await _svc.GetAsync(id)).Should().NotBeNull();
    }

    [Fact]
    public async Task Restore_of_live_file_is_noop_false()
    {
        var id = await InsertFileAsync();
        (await _svc.RestoreAsync(id)).Should().BeFalse();
    }

    [Fact]
    public async Task Purge_of_trashed_file_deletes_row_and_blob()
    {
        var id = await InsertFileAsync();
        await _svc.TrashAsync(id);

        var purged = await _svc.DeleteAsync(id);                           // DeleteAsync == purge

        purged.Should().BeTrue();
        _storage.Deletes.Should().Be(1);                                  // blob deleted on purge
        (await _db.Queryable<File>().ClearFilter<ISoftDeletable>().In(id).AnyAsync()).Should().BeFalse();
    }
}
