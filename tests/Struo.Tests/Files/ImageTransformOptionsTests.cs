using AwesomeAssertions;
using Struo.Application.Files;
using Xunit;

namespace Struo.Tests.Files;

public class ImageTransformOptionsTests
{
    [Fact]
    public void Defaults_are_safe()
    {
        // AllowedFormats itself is [] on a raw FileStorageOptions — ConfigurationBinder appends bound
        // array elements to a non-empty property default instead of replacing it, so the safe formats
        // live in ImageTransformOptions.DefaultAllowedFormats and are applied by ApplyCollectionDefaults
        // (called via PostConfigure after binding, in production). Calling it here reproduces exactly
        // what a deployment that never configures AllowedFormats ends up with.
        var o = new FileStorageOptions().ImageTransform;
        o.ApplyCollectionDefaults();
        o.Enabled.Should().BeTrue();
        o.MaxWidth.Should().Be(4096);
        o.MaxHeight.Should().Be(4096);
        o.DefaultQuality.Should().Be(82);
        o.AllowedFormats.Should().Contain(["webp", "jpeg", "png", "avif"]);
    }
}
