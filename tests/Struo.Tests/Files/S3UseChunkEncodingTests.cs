using Amazon.S3;
using Amazon.S3.Model;
using AwesomeAssertions;
using Struo.Application.Files;
using Struo.Infrastructure.Files;
using Xunit;

namespace Struo.Tests.Files;

// UseChunkEncoding decides whether an upload is signed as an aws-chunked stream or with the
// payload's SHA-256. Asserts the PutObjectRequest the storage builds; no live S3/MinIO needed.
public class S3UseChunkEncodingTests
{
    private sealed class CapturingS3Client : AmazonS3Client
    {
        public PutObjectRequest? LastPut;

        public CapturingS3Client()
            : base("ak", "sk", new AmazonS3Config { ServiceURL = "http://localhost:9000", ForcePathStyle = true }) { }

        public override Task<PutObjectResponse> PutObjectAsync(
            PutObjectRequest request, CancellationToken cancellationToken = default)
        {
            LastPut = request;
            return Task.FromResult(new PutObjectResponse());
        }
    }

    [Fact]
    public void Default_is_true() =>
        new FileStorageOptions.S3Options().UseChunkEncoding.Should().BeTrue();

    // The SDK default is true, so only the false row fails if SaveAsync stops setting the property.
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task SaveAsync_passes_the_setting_to_the_put_request(bool useChunkEncoding)
    {
        var client = new CapturingS3Client();
        using var storage = new S3FileStorage(client, "bucket", useChunkEncoding);

        await storage.SaveAsync("k/x.png", new MemoryStream([1, 2, 3]), "image/png");

        client.LastPut!.UseChunkEncoding.Should().Be(useChunkEncoding);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Public_ctor_takes_the_setting_from_the_options(bool useChunkEncoding)
    {
        var options = new FileStorageOptions
        {
            Backend = "s3",
            S3 = new()
            {
                Endpoint = "http://localhost:9000", Bucket = "b", AccessKey = "a", SecretKey = "s",
                UseChunkEncoding = useChunkEncoding,
            },
        };
        using var storage = new S3FileStorage(options);

        storage.UseChunkEncoding.Should().Be(useChunkEncoding);
    }

    [Fact]
    public async Task Seam_without_the_setting_keeps_chunk_encoding_on()
    {
        var client = new CapturingS3Client();
        using var storage = new S3FileStorage(client, "bucket");

        await storage.SaveAsync("k/x.png", new MemoryStream([1]), "image/png");

        client.LastPut!.UseChunkEncoding.Should().BeTrue();
    }
}
