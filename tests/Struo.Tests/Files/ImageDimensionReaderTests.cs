using AwesomeAssertions;
using Struo.Infrastructure.Files;
using Xunit;

namespace Struo.Tests.Files;

public class ImageDimensionReaderTests
{
    // A real 1x1 PNG.
    private static readonly byte[] Png1x1 = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg==");

    [Fact]
    public void Reads_png_dimensions() =>
        new ImageDimensionReader().TryRead(new MemoryStream(Png1x1), "image/png").Should().Be((1, 1));

    [Fact]
    public void Reads_avif_dimensions_from_the_ispe_box()
    {
        byte[] avif =
        [
            0, 0, 0, 16, (byte)'f', (byte)'t', (byte)'y', (byte)'p', (byte)'a', (byte)'v', (byte)'i', (byte)'f', 0, 0, 0, 0,
            0, 0, 0, 20, (byte)'i', (byte)'s', (byte)'p', (byte)'e', 0, 0, 0, 0,
            0, 0, 0x1F, 0x40, 0, 0, 0x17, 0x70
        ];
        new ImageDimensionReader().TryRead(new MemoryStream(avif), "image/avif").Should().Be((8000, 6000));
    }

    [Fact]
    public void Avif_reports_the_largest_ispe_so_a_small_tile_cannot_hide_a_large_grid()
    {
        byte[] avif =
        [
            0, 0, 0, 16, (byte)'f', (byte)'t', (byte)'y', (byte)'p', (byte)'a', (byte)'v', (byte)'i', (byte)'f', 0, 0, 0, 0,
            0, 0, 0, 20, (byte)'i', (byte)'s', (byte)'p', (byte)'e', 0, 0, 0, 0,
            0, 0, 0, 64, 0, 0, 0, 64,
            0, 0, 0, 20, (byte)'i', (byte)'s', (byte)'p', (byte)'e', 0, 0, 0, 0,
            0, 0, 0x1F, 0x40, 0, 0, 0x17, 0x70
        ];
        new ImageDimensionReader().TryRead(new MemoryStream(avif), "image/avif").Should().Be((8000, 6000));
    }

    [Fact]
    public void Avif_scans_past_the_first_64_KB_for_a_larger_ispe()
    {
        var ms = new MemoryStream();
        void W(params byte[] b) => ms.Write(b);
        W(0, 0, 0, 16, (byte)'f', (byte)'t', (byte)'y', (byte)'p', (byte)'a', (byte)'v', (byte)'i', (byte)'f', 0, 0, 0, 0);
        W(0, 0, 0, 20, (byte)'i', (byte)'s', (byte)'p', (byte)'e', 0, 0, 0, 0, 0, 0, 0, 64, 0, 0, 0, 64);
        W(new byte[70_000]);
        W(0, 0, 0, 20, (byte)'i', (byte)'s', (byte)'p', (byte)'e', 0, 0, 0, 0, 0, 0, 0x1F, 0x40, 0, 0, 0x17, 0x70);

        new ImageDimensionReader().TryRead(new MemoryStream(ms.ToArray()), "image/avif").Should().Be((8000, 6000));
    }

    [Fact]
    public void Reads_a_real_encoder_avif()
    {
        var bytes = NetVips.Image.Black(64, 48).Cast(NetVips.Enums.BandFormat.Uchar).WriteToBuffer(".avif");

        new ImageDimensionReader().TryRead(new MemoryStream(bytes), "image/avif").Should().Be((64, 48));
    }

    private static byte[] GifWithFrame(int screenW, int screenH, int left, int frameW, int frameH)
    {
        var ms = new MemoryStream();
        void W(params byte[] b) => ms.Write(b);
        W((byte)'G', (byte)'I', (byte)'F', (byte)'8', (byte)'9', (byte)'a');
        W((byte)screenW, (byte)(screenW >> 8), (byte)screenH, (byte)(screenH >> 8), 0x80, 0, 0);
        W(0, 0, 0, 255, 255, 255);
        W(0x21, 0xF9, 4, 0, 0, 0, 0, 0);
        W(0x2C, (byte)left, (byte)(left >> 8), 0, 0,
            (byte)frameW, (byte)(frameW >> 8), (byte)frameH, (byte)(frameH >> 8), 0);
        W(2, 2, 0x44, 0x01, 0);
        W(0x3B);
        return ms.ToArray();
    }

    [Fact]
    public void Gif_reports_the_canvas_libvips_builds_when_a_frame_exceeds_the_logical_screen()
    {
        var gif = GifWithFrame(screenW: 1, screenH: 1, left: 0, frameW: 1000, frameH: 1000);
        new ImageDimensionReader().TryRead(new MemoryStream(gif), "image/gif").Should().Be((1000, 1000));
    }

    [Fact]
    public void Gif_canvas_includes_the_frame_offset()
    {
        var gif = GifWithFrame(screenW: 1, screenH: 1, left: 500, frameW: 10, frameH: 10);
        new ImageDimensionReader().TryRead(new MemoryStream(gif), "image/gif").Should().Be((510, 10));
    }

    [Fact]
    public void Reads_gif_dimensions()
    {
        // GIF89a header, logical screen 3x2 (little-endian uint16), then trailing bytes.
        byte[] gif =
        [
            (byte)'G', (byte)'I', (byte)'F', (byte)'8', (byte)'9', (byte)'a',
            0x03, 0x00, 0x02, 0x00, 0x00, 0x00, 0x00
        ];
        new ImageDimensionReader().TryRead(new MemoryStream(gif), "image/gif").Should().Be((3, 2));
    }

    [Fact]
    public void Returns_null_for_non_image()
    {
        var bytes = new MemoryStream(System.Text.Encoding.UTF8.GetBytes("not an image at all"));
        new ImageDimensionReader().TryRead(bytes, "text/plain").Should().BeNull();
    }
}
