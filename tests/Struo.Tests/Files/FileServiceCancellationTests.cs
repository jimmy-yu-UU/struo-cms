using AwesomeAssertions;
using SqlSugar;
using Struo.Application.Configuration;
using Struo.Application.Files;
using Struo.Infrastructure.Files;
using Struo.Infrastructure.Persistence;
using Struo.Tests.Support;
using Xunit;
using File = Struo.Infrastructure.Files.File;

namespace Struo.Tests.Files;

// CS-3: FileService by-id read must forward the CancellationToken to the ORM query so an
// already-cancelled request stops at the DB call instead of running to completion.
public class FileServiceCancellationTests : IDisposable
{
    private sealed class NoopStorage : IFileStorage
    {
        public Task SaveAsync(string key, Stream content, CancellationToken ct = default) => Task.CompletedTask;
        public Task<Stream> OpenReadAsync(string key, CancellationToken ct = default) => Task.FromResult<Stream>(new MemoryStream());
        public Task DeleteAsync(string key, CancellationToken ct = default) => Task.CompletedTask;
        public bool SupportsPresignedUrls => false;
        public Task<string?> GetPresignedUrlAsync(string key, TimeSpan ttl, CancellationToken ct = default) => Task.FromResult<string?>(null);
    }

    private sealed class NoopImages : IImageDimensionReader
    {
        public (int Width, int Height)? TryRead(Stream seekable, string contentType) => null;
    }

    private readonly SqliteTestDatabase _file = new();
    private readonly ISqlSugarClient _db;
    private readonly FileService _svc;

    public FileServiceCancellationTests()
    {
        _db = SqlSugarClientFactory.Create(
            new DatabaseOptions { DbType = StruoDbType.Sqlite, ConnectionString = _file.ConnectionString },
            new TestCurrentUserAccessor(Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa")));
        _db.CodeFirst.InitTables<File>();
        _svc = new FileService(_db, new NoopStorage(), new NoopImages(), new FileStorageOptions(), null!);
    }

    public void Dispose() => _file.Dispose();

    [Fact]
    public async Task GetAsync_honors_cancellation()
    {
        var id = Guid.NewGuid();
        await _db.Insertable(new File
        {
            Id = id, StorageKey = "k", FileName = "f.bin", ContentType = "application/octet-stream",
            Size = 1, Status = "published",
        }).ExecuteCommandAsync();

        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var act = async () => await _svc.GetAsync(id, cts.Token);
        await act.Should().ThrowAsync<OperationCanceledException>();
    }
}
