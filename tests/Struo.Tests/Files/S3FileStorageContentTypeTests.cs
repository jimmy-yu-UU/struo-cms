using Amazon.S3;
using Amazon.S3.Model;
using AwesomeAssertions;
using Struo.Infrastructure.Files;
using Xunit;

namespace Struo.Tests.Files;

// SEC-6(b)(c): the S3 backend must record the validated content type as object metadata (so a
// direct/presigned GET serves the correct Content-Type instead of application/octet-stream) and must
// force a download disposition on presigned GETs (defence in depth for types the API path can't wrap,
// e.g. SVG). Asserts request assembly against a captured IAmazonS3 — no live S3/MinIO needed.
// (The env-gated S3FileStorageTests still cover a real MinIO round-trip when configured.)
public class S3FileStorageContentTypeTests
{
    // PutObjectAsync is virtual (capture the request); GetPreSignedURL is NOT virtual, so the base
    // SDK computes the signed URL string offline — the ResponseHeaderOverrides show up as query
    // params on that URL, which is what the presign test asserts.
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
    public async Task SaveAsync_records_the_content_type_on_the_put_request()
    {
        var client = new CapturingS3Client();
        using var storage = new S3FileStorage(client, "bucket");

        await storage.SaveAsync("k/x.png", new MemoryStream([1, 2, 3]), "image/png");

        client.LastPut.Should().NotBeNull();
        client.LastPut!.ContentType.Should().Be("image/png");
        client.LastPut.BucketName.Should().Be("bucket");
        client.LastPut.Key.Should().Be("k/x.png");
    }

    [Fact]
    public async Task GetPresignedUrl_forces_attachment_content_disposition()
    {
        var client = new CapturingS3Client();
        using var storage = new S3FileStorage(client, "bucket");

        var url = await storage.GetPresignedUrlAsync("k/x.svg", TimeSpan.FromMinutes(5));

        // The SDK encodes ResponseHeaderOverrides.ContentDisposition as the standard
        // `response-content-disposition` query parameter on the signed URL.
        url.Should().NotBeNull();
        url!.Should().Contain("response-content-disposition=attachment");
        url.Should().Contain("k/x.svg");
    }
}
