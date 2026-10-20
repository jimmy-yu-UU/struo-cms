using AwesomeAssertions;
using NetVips;
using Struo.Application.Files;
using Struo.Infrastructure.Files;
using Xunit;

namespace Struo.Tests.Files;

public class NetVipsImageTransformerTests
{
    private const long NoPixelLimit = long.MaxValue;

    private static byte[] Encode(int w, int h, string suffix) =>
        Image.Black(w, h).Cast(Enums.BandFormat.Uchar).WriteToBuffer(suffix);

    private static byte[] Png(int w, int h) => Encode(w, h, ".png");

    [Fact]
    public void Resizes_to_requested_width_preserving_aspect()
    {
        var src = Png(200, 100);
        var r = new NetVipsImageTransformer().Transform(src,
            new ImageTransformRequest(Width: 100, Height: null, Format: "png", Fit: "inside", Quality: 82), NoPixelLimit)!.Value;
        r.Width.Should().Be(100);
        r.Height.Should().Be(50);
        r.ContentType.Should().Be("image/png");
    }

    [Fact]
    public void Converts_format_to_webp()
    {
        var src = Png(50, 50);
        var r = new NetVipsImageTransformer().Transform(src,
            new ImageTransformRequest(Width: 40, Height: null, Format: "webp", Fit: "inside", Quality: 70), NoPixelLimit)!.Value;
        r.ContentType.Should().Be("image/webp");
    }

    // fit=cover with both dimensions fills the box with a centre crop: a 200x100 source in a
    // 100x100 box comes out at exactly 100x100.
    [Fact]
    public void Cover_fit_with_both_dimensions_crops_to_exact_box()
    {
        var src = Png(200, 100);
        var r = new NetVipsImageTransformer().Transform(src,
            new ImageTransformRequest(Width: 100, Height: 100, Format: "png", Fit: "cover", Quality: 82), NoPixelLimit)!.Value;
        r.Width.Should().Be(100);
        r.Height.Should().Be(100);
    }

    [Theory]
    [InlineData(".jpg")]
    [InlineData(".png")]
    [InlineData(".webp")]
    [InlineData(".avif")]
    public void Encoder_output_under_the_pixel_limit_is_transformed(string suffix)
    {
        var r = new NetVipsImageTransformer().Transform(Encode(300, 200, suffix),
            new ImageTransformRequest(Width: 128, Height: null, Format: "png", Fit: "inside", Quality: 82), 300 * 200);
        r.Should().NotBeNull();
        r!.Value.Width.Should().Be(128);
    }

    [Theory]
    [InlineData(128, null)]
    [InlineData(null, 64)]
    [InlineData(null, null)]
    public void Source_over_the_pixel_limit_is_not_transformed(int? width, int? height)
    {
        var r = new NetVipsImageTransformer().Transform(Png(300, 200),
            new ImageTransformRequest(width, height, "png", "inside", 82), 300 * 200 - 1);
        r.Should().BeNull();
    }

    // The ispe is rewritten to 4000x3000: libvips reports that size from the header, so the limit
    // applies before any pixel is decoded (decoding the 300x200 payload at that size would throw).
    [Fact]
    public void Limit_uses_the_size_libvips_reads_from_the_header()
    {
        var avif = Encode(300, 200, ".avif");
        var at = IndexOf(avif, "ispe"u8);
        byte[] size = [0, 0, 0x0F, 0xA0, 0, 0, 0x0B, 0xB8];
        size.CopyTo(avif, at + 8);

        var r = new NetVipsImageTransformer().Transform(avif,
            new ImageTransformRequest(128, null, "png", "inside", 82), 1_000_000);
        r.Should().BeNull();
    }

    private static int IndexOf(byte[] haystack, ReadOnlySpan<byte> needle) =>
        haystack.AsSpan().IndexOf(needle);
}
