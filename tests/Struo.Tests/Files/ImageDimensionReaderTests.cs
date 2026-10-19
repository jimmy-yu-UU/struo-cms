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

    private static byte[] AvifWithInfe(byte version, string itemType, bool truncate = false)
    {
        var ms = new MemoryStream();
        void W(params byte[] b) => ms.Write(b);
        W(0, 0, 0, 16, (byte)'f', (byte)'t', (byte)'y', (byte)'p', (byte)'a', (byte)'v', (byte)'i', (byte)'f', 0, 0, 0, 0);
        W(0, 0, 0, 20, (byte)'i', (byte)'s', (byte)'p', (byte)'e', 0, 0, 0, 0, 0, 0, 0, 64, 0, 0, 0, 64);
        W(0, 0, 0, 21, (byte)'i', (byte)'n', (byte)'f', (byte)'e', version, 0, 0, 0);
        if (version == 3) W(0, 0, 0, 1); else W(0, 1);          // item_ID
        W(0, 0);                                                // item_protection_index
        W(System.Text.Encoding.ASCII.GetBytes(itemType));
        var bytes = ms.ToArray();
        return truncate ? bytes[..^2] : bytes;
    }

    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    public void Avif_with_a_grid_item_is_not_safely_sizable(byte version) =>
        new ImageDimensionReader().TryRead(new MemoryStream(AvifWithInfe(version, "grid")), "image/avif")
            .Should().BeNull();

    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    public void Avif_with_only_ordinary_items_is_sized(byte version) =>
        new ImageDimensionReader().TryRead(new MemoryStream(AvifWithInfe(version, "av01")), "image/avif")
            .Should().Be((64, 64));

    [Fact]
    public void A_truncated_infe_box_does_not_throw_and_does_not_hide_the_ispe()
    {
        var bytes = AvifWithInfe(2, "grid", truncate: true);
        new ImageDimensionReader().TryRead(new MemoryStream(bytes), "image/avif").Should().Be((64, 64));
    }

    private static byte[] AvifWithTrailer(params byte[] trailer)
    {
        var ms = new MemoryStream();
        ms.Write([0, 0, 0, 16, (byte)'f', (byte)'t', (byte)'y', (byte)'p', (byte)'a', (byte)'v', (byte)'i', (byte)'f', 0, 0, 0, 0]);
        ms.Write([0, 0, 0, 20, (byte)'i', (byte)'s', (byte)'p', (byte)'e', 0, 0, 0, 0, 0, 0, 0, 64, 0, 0, 0, 64]);
        ms.Write(trailer);
        return ms.ToArray();
    }

    [Fact]
    public void Avif_with_a_grid_infe_using_a_64_bit_box_size_is_not_safely_sizable()
    {
        // size field 1: an 8-byte largesize sits before the version byte.
        var bytes = AvifWithTrailer(
            0, 0, 0, 1, (byte)'i', (byte)'n', (byte)'f', (byte)'e', 0, 0, 0, 0, 0, 0, 0, 29,
            2, 0, 0, 0, 0, 1, 0, 0, (byte)'g', (byte)'r', (byte)'i', (byte)'d');
        new ImageDimensionReader().TryRead(new MemoryStream(bytes), "image/avif").Should().BeNull();
    }

    [Fact]
    public void Avif_with_an_overlay_item_is_not_safely_sizable() =>
        new ImageDimensionReader().TryRead(new MemoryStream(AvifWithInfe(2, "iovl")), "image/avif")
            .Should().BeNull();

    [Fact]
    public void Avif_with_a_derived_image_reference_is_not_safely_sizable()
    {
        var bytes = AvifWithTrailer(
            0, 0, 0, 22, (byte)'i', (byte)'r', (byte)'e', (byte)'f', 0, 0, 0, 0,
            0, 0, 0, 10, (byte)'d', (byte)'i', (byte)'m', (byte)'g', 0, 1);
        new ImageDimensionReader().TryRead(new MemoryStream(bytes), "image/avif").Should().BeNull();
    }

    [Fact]
    public void Avif_scan_has_no_fixed_size_cap()
    {
        var ms = new MemoryStream();
        ms.Write([0, 0, 0, 16, (byte)'f', (byte)'t', (byte)'y', (byte)'p', (byte)'a', (byte)'v', (byte)'i', (byte)'f', 0, 0, 0, 0]);
        ms.Write(new byte[70 * 1024 * 1024]);
        ms.Write([0, 0, 0, 20, (byte)'i', (byte)'s', (byte)'p', (byte)'e', 0, 0, 0, 0, 0, 0, 0x1F, 0x40, 0, 0, 0x17, 0x70]);

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
