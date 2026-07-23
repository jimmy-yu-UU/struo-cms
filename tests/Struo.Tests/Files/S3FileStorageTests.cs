using System.Text;
using Amazon.S3;
using AwesomeAssertions;
using Struo.Application.Files;
using Struo.Infrastructure.Files;
using Xunit;

namespace Struo.Tests.Files;

// Env-gated: runs a real round-trip only when MinIO/S3 connection info is provided, e.g.
//   STRUO_S3_ENDPOINT=http://localhost:9000 STRUO_S3_BUCKET=struo
//   STRUO_S3_ACCESS=minioadmin STRUO_S3_SECRET=minioadmin
// Otherwise it no-ops so the default suite stays green without external infrastructure.
public class S3FileStorageTests
{
    private static FileStorageOptions? FromEnv()
    {
        var ep = Environment.GetEnvironmentVariable("STRUO_S3_ENDPOINT");
        var bucket = Environment.GetEnvironmentVariable("STRUO_S3_BUCKET");
        var access = Environment.GetEnvironmentVariable("STRUO_S3_ACCESS");
        var secret = Environment.GetEnvironmentVariable("STRUO_S3_SECRET");
        if (string.IsNullOrEmpty(ep) || string.IsNullOrEmpty(bucket) ||
            string.IsNullOrEmpty(access) || string.IsNullOrEmpty(secret)) return null;
        return new FileStorageOptions
        {
            Backend = "s3",
            S3 = new() { Endpoint = ep, Bucket = bucket, AccessKey = access, SecretKey = secret, ForcePathStyle = true }
        };
    }

    private static async Task EnsureBucketAsync(FileStorageOptions opts)
    {
        using var admin = new AmazonS3Client(opts.S3.AccessKey, opts.S3.SecretKey, new AmazonS3Config
        {
            ServiceURL = opts.S3.Endpoint, ForcePathStyle = opts.S3.ForcePathStyle, AuthenticationRegion = opts.S3.Region
        });
        try { await admin.PutBucketAsync(opts.S3.Bucket); }
        catch (AmazonS3Exception e) when (e.ErrorCode is "BucketAlreadyOwnedByYou" or "BucketAlreadyExists") { /* idempotent */ }
    }

    [Fact]
    public async Task Save_read_delete_and_presign()
    {
        var opts = FromEnv();
        if (opts is null) return; // skip when MinIO env not configured

        await EnsureBucketAsync(opts);

        using var s = new S3FileStorage(opts);
        var key = "it/" + Guid.NewGuid().ToString("N") + ".txt";

        await s.SaveAsync(key, new MemoryStream(Encoding.UTF8.GetBytes("s3-hello")), "text/plain");
        await using (var read = await s.OpenReadAsync(key))
        using (var sr = new StreamReader(read))
            (await sr.ReadToEndAsync()).Should().Be("s3-hello");

        s.SupportsPresignedUrls.Should().BeTrue();
        (await s.GetPresignedUrlAsync(key, TimeSpan.FromMinutes(5)))!.Should().Contain(key);

        await s.DeleteAsync(key);
    }

    [Theory]
    [InlineData("http://localhost:9000", true)]
    [InlineData("https://minio.example.com", false)]
    [InlineData(null, false)]
    public void CreateClient_derives_UseHttp_from_endpoint_scheme(string? endpoint, bool expectedUseHttp)
    {
        // A presigned URL must carry the endpoint's real scheme: the AWS SDK defaults to https,
        // which dev MinIO (http-only) refuses after the 302.
        var s3 = new FileStorageOptions.S3Options
        {
            Endpoint = endpoint, Bucket = "b", AccessKey = "k", SecretKey = "s",
        };
        using var client = (Amazon.S3.AmazonS3Client)S3FileStorage.CreateClient(s3);
        ((Amazon.S3.AmazonS3Config)client.Config).UseHttp.Should().Be(expectedUseHttp);
    }
}
