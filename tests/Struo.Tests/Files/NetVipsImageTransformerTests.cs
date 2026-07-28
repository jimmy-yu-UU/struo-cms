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

    // Fix 2: fit=cover with both width and height must genuinely fill the box (centre-crop),
    // not just allow upscaling like "inside" does. A 200x100 (2:1) source cropped to a 100x100
    // (1:1) box must come out at exactly 100x100 — "inside"/"contain" would instead stop at
    // 100x50 (preserving the source aspect ratio, no crop).
    [Fact]
    public void Cover_fit_with_both_dimensions_crops_to_exact_box()
    {
        var src = Png(200, 100);
        var r = new NetVipsImageTransformer().Transform(src,
            new ImageTransformRequest(Width: 100, Height: 100, Format: "png", Fit: "cover", Quality: 82));
        r.Width.Should().Be(100);
        r.Height.Should().Be(100);
    }
}
