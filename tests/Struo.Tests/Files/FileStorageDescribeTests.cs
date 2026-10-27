// tests/Struo.Tests/Files/FileStorageDescribeTests.cs
using AwesomeAssertions;
using Struo.Application.Files;
using Struo.Infrastructure.Files;
using Xunit;

namespace Struo.Tests.Files;

/// <summary>What <c>files:sweep</c> prints for a storage: its target, never a credential.</summary>
public class FileStorageDescribeTests
{
    private sealed class ForkStorage : IFileStorage
    {
        public Task SaveAsync(string key, Stream content, string contentType, CancellationToken ct = default) => Task.CompletedTask;
        public Task<Stream> OpenReadAsync(string key, CancellationToken ct = default) => throw new NotSupportedException();
        public Task DeleteAsync(string key, CancellationToken ct = default) => Task.CompletedTask;
        public IAsyncEnumerable<StoredBlob> ListAsync(CancellationToken ct = default) => throw new NotSupportedException();
        public bool SupportsPresignedUrls => false;
        public Task<string?> GetPresignedUrlAsync(string key, TimeSpan ttl, CancellationToken ct = default) => Task.FromResult<string?>(null);
    }

    [Fact]
    public void A_storage_that_does_not_describe_itself_is_named_by_its_type() =>
        ((IFileStorage)new ForkStorage()).Describe().Should().Be(nameof(ForkStorage));

    [Fact]
    public void Local_names_its_root()
    {
        var root = Path.Combine(Path.GetTempPath(), "describe-root");

        var text = new LocalFileStorage(new FileStorageOptions { Local = { RootPath = root } }).Describe();

        text.Should().StartWith("local").And.Contain(Path.GetFullPath(root));
    }

    [Fact]
    public void S3_names_the_bucket_and_service_but_no_credential()
    {
        var options = new FileStorageOptions
        {
            Backend = "s3",
            S3 =
            {
                Endpoint = "https://minio.example.test:9443/base", Bucket = "media-bucket",
                AccessKey = "AKIA-ACCESS-9", SecretKey = "S3-SECRET-7",
            },
        };
        using var storage = new S3FileStorage(options);

        var text = storage.Describe();

        text.Should().StartWith("s3").And.Contain("media-bucket").And.Contain("minio.example.test:9443");
        text.Should().NotContain("AKIA-ACCESS-9").And.NotContain("S3-SECRET-7").And.NotContain("/base");
    }

    [Fact]
    public void S3_never_prints_userinfo_from_the_endpoint()
    {
        var options = new FileStorageOptions
        {
            Backend = "s3",
            S3 = { Endpoint = "https://svc-user:svc-pass@minio.example.test:9000", Bucket = "b", AccessKey = "a", SecretKey = "s" },
        };
        using var storage = new S3FileStorage(options);

        storage.Describe().Should().Contain("minio.example.test:9000")
            .And.NotContain("svc-user").And.NotContain("svc-pass");
    }

    [Fact]
    public void S3_without_an_endpoint_names_the_aws_region()
    {
        var options = new FileStorageOptions
        {
            Backend = "s3",
            S3 = { Endpoint = null, Bucket = "media-bucket", AccessKey = "a", SecretKey = "s", Region = "eu-west-1" },
        };
        using var storage = new S3FileStorage(options);

        storage.Describe().Should().Contain("media-bucket").And.Contain("AWS region eu-west-1");
    }

    [Fact]
    public void S3_on_the_default_port_names_the_host_only()
    {
        var options = new FileStorageOptions
        {
            Backend = "s3",
            S3 = { Endpoint = "https://minio.example.test", Bucket = "b", AccessKey = "a", SecretKey = "s" },
        };
        using var storage = new S3FileStorage(options);

        storage.Describe().Should().EndWith("service minio.example.test");
    }
}
