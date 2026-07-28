using AwesomeAssertions;
using Struo.Application.Files;
using Xunit;

namespace Struo.Tests.Files;

public class ImageTransformOptionsTests
{
    [Fact]
    public void Defaults_are_safe()
    {
        var o = new FileStorageOptions().ImageTransform;
        o.Enabled.Should().BeTrue();
        o.MaxWidth.Should().Be(4096);
        o.MaxHeight.Should().Be(4096);
        o.DefaultQuality.Should().Be(82);
        o.AllowedFormats.Should().Contain(["webp", "jpeg", "png", "avif"]);
    }
}
