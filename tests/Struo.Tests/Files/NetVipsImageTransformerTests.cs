using AwesomeAssertions;
using NetVips;
using Struo.Application.Files;
using Struo.Infrastructure.Files;
using Xunit;

namespace Struo.Tests.Files;

public class NetVipsImageTransformerTests
{
    private static byte[] Png(int w, int h) =>
        Image.Black(w, h).Cast(Enums.BandFormat.Uchar).WriteToBuffer(".png");

    [Fact]
    public void Resizes_to_requested_width_preserving_aspect()
    {
        var src = Png(200, 100);
        var r = new NetVipsImageTransformer().Transform(src,
            new ImageTransformRequest(Width: 100, Height: null, Format: "png", Fit: "inside", Quality: 82));
        r.Width.Should().Be(100);
        r.Height.Should().Be(50);
        r.ContentType.Should().Be("image/png");
    }

    [Fact]
    public void Converts_format_to_webp()
    {
        var src = Png(50, 50);
        var r = new NetVipsImageTransformer().Transform(src,
            new ImageTransformRequest(Width: 40, Height: null, Format: "webp", Fit: "inside", Quality: 70));
        r.ContentType.Should().Be("image/webp");
    }
}
