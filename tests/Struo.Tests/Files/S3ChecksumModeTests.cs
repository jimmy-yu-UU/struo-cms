using Amazon.Runtime;
using AwesomeAssertions;
using Struo.Application.Files;
using Struo.Infrastructure.Files;
using Xunit;

namespace Struo.Tests.Files;

/// <summary>The S3 checksum mode decides whether the SDK adds checksums to every request and
/// validates them on every response, or only where the operation requires one.</summary>
public class S3ChecksumModeTests
{
    private static FileStorageOptions.S3Options S3(string? mode = null) =>
        mode is null
            ? new() { Endpoint = "http://localhost:9000" }
            : new() { Endpoint = "http://localhost:9000", ChecksumMode = mode };

    [Fact]
    public void Default_is_WhenRequired() =>
        new FileStorageOptions.S3Options().ChecksumMode.Should().Be("WhenRequired");

    [Fact]
    public void Default_config_calculates_and_validates_checksums_only_when_required()
    {
        var config = S3FileStorage.BuildConfig(S3());
        config.RequestChecksumCalculation.Should().Be(RequestChecksumCalculation.WHEN_REQUIRED);
        config.ResponseChecksumValidation.Should().Be(ResponseChecksumValidation.WHEN_REQUIRED);
    }

    [Fact]
    public void WhenSupported_config_calculates_and_validates_checksums_whenever_supported()
    {
        var config = S3FileStorage.BuildConfig(S3("WhenSupported"));
        config.RequestChecksumCalculation.Should().Be(RequestChecksumCalculation.WHEN_SUPPORTED);
        config.ResponseChecksumValidation.Should().Be(ResponseChecksumValidation.WHEN_SUPPORTED);
    }

    [Theory]
    [InlineData("WhenRequired")]
    [InlineData("WhenSupported")]
    public void Allowed_mode_passes_validation(string mode) =>
        new FileStorageOptions { Backend = "s3", S3 = Valid(mode) }
            .Invoking(x => x.Validate()).Should().NotThrow();

    [Theory]
    [InlineData("Always")]
    [InlineData("whenrequired")]
    [InlineData("")]
    public void Unknown_mode_throws_naming_the_key(string mode) =>
        new FileStorageOptions { Backend = "s3", S3 = Valid(mode) }
            .Invoking(x => x.Validate()).Should().Throw<InvalidOperationException>()
            .WithMessage("*Struo:Files:S3:ChecksumMode*");

    private static FileStorageOptions.S3Options Valid(string mode) => new()
    {
        Endpoint = "http://x", Bucket = "b", AccessKey = "a", SecretKey = "s", ChecksumMode = mode,
    };
}
