// tests/Struo.Tests/Files/FileServiceLogoLifecycleTests.cs
using AwesomeAssertions;
using SqlSugar;
using Struo.Application.Configuration;
using Struo.Application.Files;
using Struo.Application.Query;
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

// SEC-10/DB-15/TEST-10: SettingsController only validates the logo file is "published" at SAVE
// time; nothing previously cleared site_settings.logofileid when that same file was later
// deleted, leaving a dangling reference that ConfigController would keep resolving into a dead
// /api/files/{id}/content URL. FileService.DeleteAsync must null the column, inside the same
// transaction as the file row delete, whenever the deleted file is the current logo.
public class FileServiceLogoLifecycleTests : IDisposable
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

    private static readonly Guid Tester = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");

    private readonly SqliteTestDatabase _file = new();
    private readonly ISqlSugarClient _db;
    private readonly FileService _svc;

    public FileServiceLogoLifecycleTests()
    {
        _db = SqlSugarClientFactory.Create(
            new DatabaseOptions { DbType = StruoDbType.Sqlite, ConnectionString = _file.ConnectionString },
            new TestCurrentUserAccessor(Tester));
        _db.CodeFirst.InitTables<File>();
        _db.CodeFirst.InitTables<FileTranslation>();
        _db.CodeFirst.InitTables<SiteSettings>();

        var collections = MetadataScanner.ScanTypes([typeof(File)]);
        var provider = new CachedMetadataProvider(collections);
        var registry = new EntityRegistry(MetadataScanner.ScanDescriptors([typeof(File)]));
        var collectionTypes = new Dictionary<string, Type>(StringComparer.OrdinalIgnoreCase)
        {
            ["file"] = typeof(File),
        };
        var graph = new RelationshipGraph(collections, collectionTypes);
        var repo = new SqlSugarItemRepository(_db, registry, graph, provider, new StruoQueryOptions());
        _svc = new FileService(_db, new NoopStorage(), new NoopImages(), new FileStorageOptions(), repo);
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
    public async Task Delete_of_current_logo_file_nulls_site_settings_logofileid()
    {
        var logoId = await InsertFileAsync();
        await InsertSiteSettingsAsync(logoId);
        var before = await _db.Queryable<SiteSettings>().Where(s => s.Id == SiteSettings.SingletonId).FirstAsync();

        var deleted = await _svc.DeleteAsync(logoId);

        deleted.Should().BeTrue();
        var row = await _db.Queryable<SiteSettings>().Where(s => s.Id == SiteSettings.SingletonId).FirstAsync();
        row.Should().NotBeNull();
        row!.LogoFileId.Should().BeNull();
        // SEC-10: pins the SetColumns(s => new SiteSettings { LogoFileId = null }) call to a
        // single-member update — a regression that broadened it into a full-row SetColumns (nulling
        // every column instead of just LogoFileId) would silently wipe BrandName/UpdatedAt too.
        row.BrandName.Should().Be(before!.BrandName);
        row.UpdatedAt.Should().Be(before.UpdatedAt);
    }

    [Fact]
    public async Task Delete_of_unrelated_file_leaves_site_settings_logofileid_untouched()
    {
        var currentLogoId = Guid.NewGuid();
        await InsertSiteSettingsAsync(currentLogoId);
        var otherFileId = await InsertFileAsync();

        var deleted = await _svc.DeleteAsync(otherFileId);

        deleted.Should().BeTrue();
        var row = await _db.Queryable<SiteSettings>().Where(s => s.Id == SiteSettings.SingletonId).FirstAsync();
        row!.LogoFileId.Should().Be(currentLogoId);
    }

    [Fact]
    public async Task Delete_when_no_site_settings_row_exists_still_succeeds()
    {
        var id = await InsertFileAsync();

        var deleted = await _svc.DeleteAsync(id);

        deleted.Should().BeTrue();
    }
}
