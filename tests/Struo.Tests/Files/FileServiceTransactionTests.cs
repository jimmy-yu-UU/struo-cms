using AwesomeAssertions;
using SqlSugar;
using Struo.Application.Configuration;
using Struo.Application.Files;
using Struo.Application.Query;
using Struo.Infrastructure.Metadata;
using Struo.Infrastructure.Persistence;
using Struo.Infrastructure.Files;
using Struo.Infrastructure.Query;
using Struo.Tests.Support;
using Xunit;
using File = Struo.Infrastructure.Files.File;
using FileTranslation = Struo.Infrastructure.Files.FileTranslation;

namespace Struo.Tests.Files;

// CS-8: FileService.DeleteAsync must delete the row + its translation sidecars through the
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

        var collections = MetadataScanner.ScanTypes([typeof(File)]);
        var provider = new CachedMetadataProvider(collections);
        var registry = new EntityRegistry(MetadataScanner.ScanDescriptors([typeof(File)]));
        var collectionTypes = new Dictionary<string, Type>(StringComparer.OrdinalIgnoreCase)
        {
            ["file"] = typeof(File),
        };
        var graph = new RelationshipGraph(collections, collectionTypes);
        _repo = new SqlSugarItemRepository(_db, registry, graph, provider, new StruoQueryOptions());
        _svc = new FileService(_db, new NoopStorage(), new NoopImages(), new FileStorageOptions(), _repo);
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
}
