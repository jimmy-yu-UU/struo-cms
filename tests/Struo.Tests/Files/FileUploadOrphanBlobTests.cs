using AwesomeAssertions;
using Microsoft.Extensions.Logging;
using SqlSugar;
using Struo.Application.Configuration;
using Struo.Application.Files;
using Struo.Application.Localization;
using Struo.Domain.Localization;
using Struo.Domain.Query;
using Struo.Application.Query;
using Struo.Infrastructure.Metadata;
using Struo.Infrastructure.Query;
using Struo.Infrastructure.Files;
using Struo.Infrastructure.Persistence;
using Struo.Tests.Support;
using Xunit;
using File = Struo.Infrastructure.Files.File;
using FileTranslation = Struo.Infrastructure.Files.FileTranslation;

namespace Struo.Tests.Files;

// An upload stores the blob before the row; a failed database write must not leave the blob behind.
public sealed class FileUploadOrphanBlobTests : IDisposable
{
    private sealed class RecordingStorage : IFileStorage
    {
        public readonly HashSet<string> Keys = [];
        public Action? AfterSave { get; set; }
        public bool FailDelete { get; set; }

        public Task SaveAsync(string key, Stream content, string contentType, CancellationToken ct = default)
        {
            Keys.Add(key);
            AfterSave?.Invoke();
            return Task.CompletedTask;
        }

        public Task<Stream> OpenReadAsync(string key, CancellationToken ct = default) => Task.FromResult<Stream>(new MemoryStream());

        public Task DeleteAsync(string key, CancellationToken ct = default)
        {
            if (FailDelete) throw new IOException("storage offline");
            Keys.Remove(key);
            return Task.CompletedTask;
        }

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

    private static readonly Guid Tester = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");

    private readonly ITestDatabase _file = TestBackend.CreateUnitDatabase();
    private readonly ISqlSugarClient _db;
    private readonly RecordingStorage _storage = new();
    private readonly ListLogger<FileService> _log = new();
    private readonly FileService _svc;

    public FileUploadOrphanBlobTests()
    {
        _db = SqlSugarClientFactory.Create(_file.Options(), new TestCurrentUserAccessor(Tester));
        _db.CodeFirst.InitTables<File>();
        _db.CodeFirst.InitTables<FileTranslation>();
        _db.CodeFirst.InitTables<MediaFolder>();
        var types = new[] { typeof(File), typeof(MediaFolder) };
        var collections = MetadataScanner.ScanTypes(types);
        var provider = new CachedMetadataProvider(collections);
        var registry = new EntityRegistry(MetadataScanner.ScanDescriptors(types));
        var graph = new RelationshipGraph(collections, new Dictionary<string, Type>(StringComparer.OrdinalIgnoreCase)
        {
            ["file"] = typeof(File),
            ["mediafolder"] = typeof(MediaFolder),
        });
        var repo = new SqlSugarItemRepository(_db, registry, graph, provider, new StruoQueryOptions());
        _svc = new FileService(
            _db, new FileStorageServices(_storage, new NoopImages(), new FileStorageOptions()), new FileServiceCollaborators(repo, new StubLanguages(), new TestCurrentUserAccessor(Tester)), logger: _log);
    }

    public void Dispose() => _file.Dispose();

    private Task<File> UploadAsync(CancellationToken ct = default) =>
        _svc.UploadAsync(new MemoryStream(new byte[10]), "x.bin", "application/octet-stream", 10, ct: ct);

    // Drops the file table so the row insert fails like a database outage would.
    private void BreakDatabase() =>
        _db.DbMaintenance.DropTable(_db.EntityMaintenance.GetEntityInfo<File>().DbTableName);

    [Fact]
    public async Task Database_failure_removes_the_blob_and_surfaces_the_original_exception()
    {
        BreakDatabase();

        var act = async () => await UploadAsync();

        var thrown = await act.Should().ThrowAsync<Exception>();
        thrown.Which.Should().NotBeOfType<IOException>();
        _storage.Keys.Should().BeEmpty();
    }

    [Fact]
    public async Task Cancellation_after_the_blob_save_removes_the_blob()
    {
        using var cts = new CancellationTokenSource();
        _storage.AfterSave = () => cts.Cancel();

        var act = async () => await UploadAsync(cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        _storage.Keys.Should().BeEmpty();
    }

    [Fact]
    public async Task Failed_blob_delete_is_logged_with_the_key_and_the_original_exception_surfaces()
    {
        BreakDatabase();
        _storage.FailDelete = true;

        var act = async () => await UploadAsync();

        var thrown = await act.Should().ThrowAsync<Exception>();
        thrown.Which.Should().NotBeOfType<IOException>();
        var key = _storage.Keys.Single();
        _log.Entries.Should().Contain(e => e.Level == LogLevel.Warning && e.Message.Contains(key));
    }

    [Fact]
    public async Task Successful_upload_keeps_the_blob()
    {
        var file = await UploadAsync();

        _storage.Keys.Should().ContainSingle().Which.Should().Be(file.StorageKey);
    }
}
