using System.Net;
using Amazon.S3;
using Amazon.S3.Model;
using AwesomeAssertions;
using Struo.Application.Files;
using Struo.Infrastructure.Files;
using Xunit;

namespace Struo.Tests.Files;

public sealed class FileStorageListingTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "struo-list-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    private LocalFileStorage Local() => new(new FileStorageOptions { Local = { RootPath = _root } });

    private static async Task<List<StoredBlob>> Collect(IFileStorage storage)
    {
        List<StoredBlob> all = [];
        await foreach (var blob in storage.ListAsync()) all.Add(blob);
        return all;
    }

    [Fact]
    public async Task Local_listing_of_a_missing_root_is_empty() =>
        (await Collect(Local())).Should().BeEmpty();

    [Fact]
    public async Task Local_listing_returns_forward_slash_keys_with_the_write_time()
    {
        var path = Path.Combine(_root, "2026", "09", "abc.png");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await System.IO.File.WriteAllBytesAsync(path, [1, 2, 3]);
        var stamp = new DateTime(2026, 9, 1, 8, 0, 0, DateTimeKind.Utc);
        System.IO.File.SetLastWriteTimeUtc(path, stamp);

        var blobs = await Collect(Local());

        blobs.Should().ContainSingle().Which.Should().Be(new StoredBlob("2026/09/abc.png", stamp));
    }

    private sealed class PagedS3Client(Dictionary<string, ListObjectsV2Response> pages) : AmazonS3Client(
        "ak", "sk", new AmazonS3Config { ServiceURL = "http://localhost:9000", ForcePathStyle = true })
    {
        public readonly List<string?> Tokens = [];

        public override Task<ListObjectsV2Response> ListObjectsV2Async(
            ListObjectsV2Request request, CancellationToken cancellationToken = default)
        {
            Tokens.Add(request.ContinuationToken);
            return Task.FromResult(pages[request.ContinuationToken ?? ""]);
        }
    }

    private static ListObjectsV2Response Page(string? next, params (string Key, DateTime Modified)[] objects) =>
        new()
        {
            HttpStatusCode = HttpStatusCode.OK,
            IsTruncated = next is not null,
            NextContinuationToken = next,
            S3Objects = [.. objects.Select(o => new S3Object { Key = o.Key, LastModified = o.Modified })],
        };

    [Fact]
    public async Task S3_listing_follows_every_continuation_token()
    {
        var t = new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc);
        var client = new PagedS3Client(new()
        {
            [""] = Page("p2", ("2026/09/a.png", t)),
            ["p2"] = Page("p3", ("2026/09/b.png", t.AddHours(1)), ("2026/09/c.png", t.AddHours(2))),
            ["p3"] = Page(null, ("2026/09/d.png", t.AddHours(3))),
        });
        using var storage = new S3FileStorage(client, "bucket");

        var blobs = await Collect(storage);

        blobs.Select(b => b.Key).Should().Equal("2026/09/a.png", "2026/09/b.png", "2026/09/c.png", "2026/09/d.png");
        blobs[2].LastModifiedUtc.Should().Be(t.AddHours(2));
        client.Tokens.Should().Equal(null, "p2", "p3");
    }
}
