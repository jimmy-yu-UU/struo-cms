using AwesomeAssertions;
using Struo.Application.Files;
using Struo.Infrastructure.Files;
using Xunit;

namespace Struo.Tests.Files;

// The scheme of a presigned URL follows the configured endpoint. Presigning is computed locally,
// so a real client with throwaway credentials needs no live S3.
public class S3PresignedUrlSchemeTests
{
    private static async Task<string> PresignAsync(string? endpoint)
    {
        var options = new FileStorageOptions
        {
            Backend = "s3",
            S3 = { Endpoint = endpoint, Region = "us-east-1", Bucket = "b", AccessKey = "ak", SecretKey = "sk" },
        };
        using var storage = new S3FileStorage(options);
        return (await storage.GetPresignedUrlAsync("k/x.png", TimeSpan.FromMinutes(5)))!;
    }

    [Theory]
    [InlineData("http://localhost:9000", "http://localhost:9000/")]
    [InlineData("HTTP://localhost:9000", "http://localhost:9000/")]
    [InlineData("https://minio.example.test:9443", "https://minio.example.test:9443/")]
    public async Task Presigned_url_uses_the_endpoint_scheme(string endpoint, string expectedPrefix)
    {
        var url = await PresignAsync(endpoint);

        url.Should().StartWith(expectedPrefix);
    }

    [Fact]
    public async Task Presigned_url_without_an_endpoint_is_https()
    {
        var url = await PresignAsync(null);

        url.Should().StartWith("https://");
    }
}
