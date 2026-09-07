using AwesomeAssertions;
using SqlSugar;
using Struo.Application.Configuration;
using Struo.Application.Files;
using Struo.Application.Localization;
using Struo.Application.Query;
using Struo.Domain.Localization;
using Struo.Domain.Query;
using Struo.Infrastructure.Metadata;
using Struo.Infrastructure.Persistence;
using Struo.Infrastructure.Files;
using Struo.Infrastructure.Query;
using Struo.Tests.Support;
using Xunit;
using File = Struo.Infrastructure.Files.File;
using FileTranslation = Struo.Infrastructure.Files.FileTranslation;

namespace Struo.Tests.Files;

// FileService.DeleteAsync must delete the row + its translation sidecars through the
// nesting-safe IItemRepository.InTransactionAsync helper (join-if-active), not raw
// BeginTran/CommitTran/RollbackTran. When invoked inside an outer transaction, the delete must
// join it — an outer rollback then undoes the delete. The old raw-transaction code opened (and
// committed) its own inner transaction, prematurely ending the outer unit-of-work.
public class FileServiceTransactionTests : IDisposable
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

    private static readonly Guid Tester = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");

    private readonly SqliteTestDatabase _file = new();
    private readonly ISqlSugarClient _db;
    private readonly IItemRepository _repo;
    private readonly FileService _svc;

    public FileServiceTransactionTests()
    {
        _db = SqlSugarClientFactory.Create(
            new DatabaseOptions { DbType = StruoDbType.Sqlite, ConnectionString = _file.ConnectionString },
            new TestCurrentUserAccessor(Tester));
        _db.CodeFirst.InitTables<File>();
        _db.CodeFirst.InitTables<FileTranslation>();
        // DeleteAsync now also clears site_settings.logofileid when applicable, so the
        // table must exist even though this test's own scenarios never seed a row into it.
        _db.CodeFirst.InitTables<Struo.Infrastructure.Settings.SiteSettings>();
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
        _svc = new FileService(
            _db, new FileStorageServices(new NoopStorage(), new NoopImages(), new FileStorageOptions()), _repo, new StubLanguages(),
            new TestCurrentUserAccessor(Tester));
    }

    public void Dispose() => _file.Dispose();

    private async Task<Guid> InsertFileAsync()
    {
        var id = Guid.NewGuid();
        await _db.Insertable(new File
        {
            Id = id, StorageKey = "k", FileName = "f.bin", ContentType = "application/octet-stream",
            Size = 1, Status = "published",
        }).ExecuteCommandAsync();
        return id;
    }

    [Fact]
    public async Task Delete_inside_outer_transaction_joins_it_and_is_rolled_back()
    {
        var id = await InsertFileAsync();

        var deleted = false;
        var act = async () => await _repo.InTransactionAsync(async () =>
        {
            deleted = await _svc.DeleteAsync(id);
            throw new InvalidOperationException("force-rollback");
        });

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("force-rollback");

        deleted.Should().BeTrue(
            "DeleteAsync must join the already-open transaction and report success, not fail on a nested BeginTran");
        (await _db.Queryable<File>().In(id).AnyAsync()).Should().BeTrue(
            "the outer rollback must undo the nested delete — proving DeleteAsync did not prematurely commit its own inner transaction");
    }

    [Fact]
    public async Task Delete_standalone_still_removes_the_row()
    {
        var id = await InsertFileAsync();

        var result = await _svc.DeleteAsync(id);

        result.Should().BeTrue();
        (await _db.Queryable<File>().In(id).AnyAsync()).Should().BeFalse();
    }

    // Sanity check: bounding FileBufferingReadStream to MaxUploadBytes must not break a normal
    // upload whose declared length and actual bytes agree and sit under the cap.
    [Fact]
    public async Task Upload_within_cap_still_succeeds_end_to_end()
    {
        var bytes = new byte[50];
        var file = await _svc.UploadAsync(
            new MemoryStream(bytes), "x.bin", "application/octet-stream", length: 50);

        file.Size.Should().Be(50);
        (await _db.Queryable<File>().In(file.Id).AnyAsync()).Should().BeTrue();
    }

    // The stored File.Size must reflect the ACTUAL uploaded byte count, not the
    // client-declared Content-Length — a client understating its length (both figures under the
    // cap) must not cause a wrong Size to be persisted.
    [Fact]
    public async Task Upload_declaring_smaller_length_than_actual_bytes_stores_actual_byte_count()
    {
        var actualBytes = new byte[200];
        var file = await _svc.UploadAsync(
            new MemoryStream(actualBytes), "x.bin", "application/octet-stream", length: 50 /* declared, smaller than actual */);

        file.Size.Should().Be(200, "Size must reflect the actual bytes read, not the client-declared length");
        var stored = await _db.Queryable<File>().In(file.Id).FirstAsync();
        stored!.Size.Should().Be(200);
    }

    // Exact-boundary: actual bytes exactly at MaxUploadBytes must succeed and record that
    // exact size.
    [Fact]
    public async Task Upload_with_actual_bytes_exactly_at_cap_succeeds()
    {
        var opts = new FileStorageOptions { MaxUploadBytes = 64 };
        var svc = new FileService(
            _db, new FileStorageServices(new NoopStorage(), new NoopImages(), opts), _repo, new StubLanguages(),
            new TestCurrentUserAccessor(Tester));
        var actualBytes = new byte[64];

        var file = await svc.UploadAsync(
            new MemoryStream(actualBytes), "x.bin", "application/octet-stream", length: 1 /* declared, irrelevant */);

        file.Size.Should().Be(64);
    }

    // Exact-boundary: one byte over MaxUploadBytes must throw PayloadTooLargeException,
    // even though the declared length is well under the cap.
    [Fact]
    public async Task Upload_with_actual_bytes_one_over_cap_throws_PayloadTooLarge()
    {
        var opts = new FileStorageOptions { MaxUploadBytes = 64 };
        var svc = new FileService(
            _db, new FileStorageServices(new NoopStorage(), new NoopImages(), opts), _repo, new StubLanguages(),
            new TestCurrentUserAccessor(Tester));
        var actualBytes = new byte[65];

        var act = async () => await svc.UploadAsync(
            new MemoryStream(actualBytes), "x.bin", "application/octet-stream", length: 1 /* declared, irrelevant */);

        await act.Should().ThrowAsync<PayloadTooLargeException>();
    }
}
