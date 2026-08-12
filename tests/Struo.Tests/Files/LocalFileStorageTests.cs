using System.Text;
using AwesomeAssertions;
using Struo.Application.Files;
using Struo.Domain.Query;
using Struo.Infrastructure.Files;
using Xunit;

namespace Struo.Tests.Files;

public class LocalFileStorageTests : IDisposable
{
    private readonly string _root =
        Path.Combine(Path.GetTempPath(), "struo-files-" + Guid.NewGuid().ToString("N"));

    private LocalFileStorage NewStorage() =>
        new(new FileStorageOptions { Local = new FileStorageOptions.LocalOptions { RootPath = _root } });

    [Fact]
    public async Task Save_then_read_round_trips_bytes()
    {
        var storage = NewStorage();
        var key = "2026/06/" + Guid.NewGuid().ToString("N") + ".txt";
        await storage.SaveAsync(key, new MemoryStream(Encoding.UTF8.GetBytes("hello")), "text/plain");

        await using var read = await storage.OpenReadAsync(key);
        using var sr = new StreamReader(read);
        (await sr.ReadToEndAsync()).Should().Be("hello");
    }

    [Fact]
    public async Task Delete_removes_the_file()
    {
        var storage = NewStorage();
        var key = "a/b/" + Guid.NewGuid().ToString("N");
        await storage.SaveAsync(key, new MemoryStream([1, 2, 3]), "application/octet-stream");
        await storage.DeleteAsync(key);
        var act = async () => await storage.OpenReadAsync(key);
        // The row-exists-but-blob-is-gone case must surface as the shared domain exception
        // (translated to a 404 at the API layer), not the raw FileNotFoundException.
        await act.Should().ThrowAsync<FileBlobNotFoundException>();
    }

    [Fact]
    public async Task OpenReadAsync_missing_key_throws_FileBlobNotFoundException()
    {
        var storage = NewStorage();
        // The root exists (NewStorage never creates it, so this also covers a storage root that
        // was never initialized) but the key was never written under it.
        var act = async () => await storage.OpenReadAsync("never/written.txt");
        (await act.Should().ThrowAsync<FileBlobNotFoundException>()).Which.Key.Should().Be("never/written.txt");
    }

    [Fact]
    public async Task OpenReadAsync_missing_intermediate_directory_throws_FileBlobNotFoundException()
    {
        // Reproduces the live DirectoryNotFoundException case: a key nested under a year/month
        // folder that was never created (e.g. rows imported from another backend/environment).
        var storage = NewStorage();
        Directory.CreateDirectory(_root); // root exists; "test" subdirectory under it does not
        var act = async () => await storage.OpenReadAsync("test/og.jpg");
        await act.Should().ThrowAsync<FileBlobNotFoundException>();
    }

    [Fact]
    public void Local_does_not_support_presigned_urls() =>
        NewStorage().SupportsPresignedUrls.Should().BeFalse();

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }
}
