using System.Text;
using AwesomeAssertions;
using Struo.Application.Files;
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
        await act.Should().ThrowAsync<FileNotFoundException>();
    }

    [Fact]
    public void Local_does_not_support_presigned_urls() =>
        NewStorage().SupportsPresignedUrls.Should().BeFalse();

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }
}
