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

    [Fact]
    public void Hardening_defaults_match_the_documented_values()
    {
        var o = new FileStorageOptions().ImageTransform;
        o.ApplyCollectionDefaults();
        o.SizeSteps.Should().Equal(64, 128, 256, 320, 480, 640, 768, 1024, 1280, 1600, 1920, 2560, 3840, 4096);
        o.TransformableContentTypes.Should().Equal("image/jpeg", "image/png", "image/webp", "image/avif", "image/gif");
        o.MaxConcurrentTransforms.Should().Be(2);
        o.CacheMaxBytes.Should().Be(2_147_483_648);
        o.MaxSourcePixels.Should().Be(50_000_000);
    }

    [Theory]
    [InlineData(nameof(FileStorageOptions.ImageTransformOptions.MaxConcurrentTransforms), 0)]
    [InlineData(nameof(FileStorageOptions.ImageTransformOptions.MaxConcurrentTransforms), 1)]
    [InlineData(nameof(FileStorageOptions.ImageTransformOptions.QueueTimeoutSeconds), -1)]
    [InlineData(nameof(FileStorageOptions.ImageTransformOptions.MaxSourcePixels), 0)]
    [InlineData(nameof(FileStorageOptions.ImageTransformOptions.MaxWidth), 0)]
    [InlineData(nameof(FileStorageOptions.ImageTransformOptions.MaxHeight), 0)]
    [InlineData(nameof(FileStorageOptions.ImageTransformOptions.DefaultQuality), 0)]
    [InlineData(nameof(FileStorageOptions.ImageTransformOptions.DefaultQuality), 101)]
    public void Out_of_range_hardening_values_fail_validation(string property, long value)
    {
        var o = new FileStorageOptions();
        var target = typeof(FileStorageOptions.ImageTransformOptions).GetProperty(property)!;
        target.SetValue(o.ImageTransform, Convert.ChangeType(value, target.PropertyType));

        var act = o.Validate;

        act.Should().Throw<InvalidOperationException>().WithMessage($"*{property}*");
    }

    [Fact]
    public void A_non_positive_size_step_fails_validation()
    {
        var o = new FileStorageOptions();
        o.ImageTransform.SizeSteps = [64, 0];

        var act = o.Validate;

        act.Should().Throw<InvalidOperationException>().WithMessage("*SizeSteps*");
    }
}
