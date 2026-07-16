using AwesomeAssertions;
using Struo.Application.Files;
using Struo.Domain.Query;
using Struo.Infrastructure.Files;
using Xunit;

namespace Struo.Tests.Files;

public class FileServiceValidationTests
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

    [Fact]
    public async Task Upload_over_cap_throws()
    {
        var opts = new FileStorageOptions { MaxUploadBytes = 4 };
        var svc = new FileService(null!, new NoopStorage(), new NoopImages(), opts, null!);
        var act = async () => await svc.UploadAsync(new MemoryStream(new byte[10]), "x.bin", "application/octet-stream", 10);
        await act.Should().ThrowAsync<QueryException>();
    }

    [Fact]
    public async Task Upload_disallowed_type_throws()
    {
        var opts = new FileStorageOptions { AllowedContentTypes = ["image/png"] };
        var svc = new FileService(null!, new NoopStorage(), new NoopImages(), opts, null!);
        var act = async () => await svc.UploadAsync(new MemoryStream([1]), "x.txt", "text/plain", 1);
        await act.Should().ThrowAsync<QueryException>();
    }
}
