using System.Net;
using Amazon.Runtime;
using Amazon.S3;
using AwesomeAssertions;
using Struo.Domain.Query;
using Struo.Infrastructure.Files;
using Xunit;

namespace Struo.Tests.Files;

// A missing S3 key must degrade the same way a missing local file does (FileBlobNotFoundException,
// -> HTTP 404), so the API contract doesn't depend on which storage backend is configured. Asserts
// against a captured/faulting IAmazonS3 client — no live S3/MinIO needed (same pattern as
// S3FileStorageContentTypeTests).
public class S3FileStorageMissingKeyTests
{
    private sealed class FaultingS3Client(AmazonS3Exception toThrow) : AmazonS3Client(
        "ak", "sk", new AmazonS3Config { ServiceURL = "http://localhost:9000", ForcePathStyle = true })
    {
        public override Task<Amazon.S3.Model.GetObjectResponse> GetObjectAsync(
            string bucketName, string key, CancellationToken cancellationToken = default) =>
            throw toThrow;
    }

    [Fact]
    public async Task Missing_key_throws_FileBlobNotFoundException()
    {
        var notFound = new AmazonS3Exception(
            "The specified key does not exist.", ErrorType.Sender, "NoSuchKey", "req-1", HttpStatusCode.NotFound);
        using var storage = new S3FileStorage(new FaultingS3Client(notFound), "bucket");

        var act = async () => await storage.OpenReadAsync("missing.txt");

        await act.Should().ThrowAsync<FileBlobNotFoundException>();
    }

    [Fact]
    public async Task Other_s3_failures_are_not_swallowed_into_a_404()
    {
        // A permissions failure (or any other non-missing-key S3 fault) must still propagate as-is,
        // so it surfaces as a 500 rather than being hidden behind a false "not found".
        var accessDenied = new AmazonS3Exception(
            "Access Denied.", ErrorType.Sender, "AccessDenied", "req-2", HttpStatusCode.Forbidden);
        using var storage = new S3FileStorage(new FaultingS3Client(accessDenied), "bucket");

        var act = async () => await storage.OpenReadAsync("secret.txt");

        (await act.Should().ThrowAsync<AmazonS3Exception>()).Which.ErrorCode.Should().Be("AccessDenied");
    }
}
