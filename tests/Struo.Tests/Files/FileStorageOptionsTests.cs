using AwesomeAssertions;
using Struo.Application.Files;
using Xunit;

namespace Struo.Tests.Files;

public class FileStorageOptionsTests
{
    [Fact]
    public void Local_backend_requires_root_path()
    {
        var o = new FileStorageOptions { Backend = "local", Local = new() { RootPath = "" } };
        o.Invoking(x => x.Validate()).Should().Throw<InvalidOperationException>();
    }

    private static FileStorageOptions.S3Options CompleteS3() =>
        new() { Endpoint = "http://x", Bucket = "b", AccessKey = "k", SecretKey = "s" };

    [Fact]
    public void S3_backend_with_every_connection_field_passes() =>
        new FileStorageOptions { Backend = "s3", S3 = CompleteS3() }
            .Invoking(x => x.Validate()).Should().NotThrow();

    [Theory]
    [InlineData(nameof(FileStorageOptions.S3Options.Endpoint))]
    [InlineData(nameof(FileStorageOptions.S3Options.Bucket))]
    [InlineData(nameof(FileStorageOptions.S3Options.AccessKey))]
    [InlineData(nameof(FileStorageOptions.S3Options.SecretKey))]
    public void S3_backend_requires_each_connection_field(string blankField)
    {
        var s3 = CompleteS3();
        typeof(FileStorageOptions.S3Options).GetProperty(blankField)!.SetValue(s3, null);

        new FileStorageOptions { Backend = "s3", S3 = s3 }
            .Invoking(x => x.Validate()).Should().Throw<InvalidOperationException>()
            .WithMessage("*Struo:Files:S3*");
    }

    [Fact]
    public void Custom_backend_needs_no_local_or_s3_settings() =>
        new FileStorageOptions { Backend = "custom", Local = new() { RootPath = "" } }
            .Invoking(x => x.Validate()).Should().NotThrow();

    [Fact]
    public void The_unknown_backend_message_lists_custom() =>
        new FileStorageOptions { Backend = "azure" }
            .Invoking(x => x.Validate()).Should().Throw<InvalidOperationException>()
            .WithMessage("*'azure'*'custom'*");

    [Fact]
    public void Valid_local_passes() =>
        new FileStorageOptions { Backend = "local", Local = new() { RootPath = "App_Data/uploads" } }
            .Invoking(x => x.Validate()).Should().NotThrow();

    [Fact]
    public void PresignedRedirect_defaults_to_false()
    {
        // Admin thumbnails consume /api/files/{id}/content directly; the safe default is to proxy
        // bytes through the API. Redirect-to-storage is a deployment opt-in.
        new FileStorageOptions().PresignedRedirect.Should().BeFalse();
    }

    [Fact]
    public void DefaultStatus_defaults_to_published() =>
        new FileStorageOptions().DefaultStatus.Should().Be("published");

    [Theory]
    [InlineData("published")]
    [InlineData("draft")]
    public void Allowed_default_status_passes(string status) =>
        new FileStorageOptions { DefaultStatus = status }
            .Invoking(x => x.Validate()).Should().NotThrow();

    [Theory]
    [InlineData("archived")]
    [InlineData("")]
    [InlineData("Published")]
    public void Unknown_default_status_throws_naming_the_key(string status) =>
        new FileStorageOptions { DefaultStatus = status }
            .Invoking(x => x.Validate()).Should().Throw<InvalidOperationException>()
            .WithMessage("*Struo:Files:DefaultStatus*");
}
