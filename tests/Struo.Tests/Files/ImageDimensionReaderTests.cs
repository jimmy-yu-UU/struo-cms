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

    // A first frame whose data runs well past the 64 KB header read, then a larger second frame.
    private static byte[] GifWithLargeFirstFrame()
    {
        var ms = new MemoryStream();
        void W(params byte[] b) => ms.Write(b);
        W((byte)'G', (byte)'I', (byte)'F', (byte)'8', (byte)'9', (byte)'a');
        W(1, 0, 1, 0, 0x80, 0, 0);
        W(0, 0, 0, 255, 255, 255);
        W(0x2C, 0, 0, 0, 0, 1, 0, 1, 0, 0);
        W(2);
        var block = new byte[256];
        block[0] = 255;
        for (var i = 0; i < 400; i++) ms.Write(block);   // 400 sub-blocks of 255 bytes, about 100 KB
        W(0);
        W(0x2C, 0, 0, 0, 0, 0xE8, 0x03, 0xD0, 0x07, 0);
        W(2, 2, 0x44, 0x01, 0);
        W(0x3B);
        return ms.ToArray();
    }

    [Fact]
    public void Gif_walk_works_on_a_stream_that_is_not_fully_buffered()
    {
        using var buffered = new Microsoft.AspNetCore.WebUtilities.FileBufferingReadStream(
            new MemoryStream(GifWithLargeFirstFrame()), memoryThreshold: 4096);

        new ImageDimensionReader().TryRead(buffered, "image/gif").Should().Be((1000, 2000));
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

    // Builds a JPEG from marker segments; a segment's payload is zero-filled to the requested size.
    internal static byte[] Jpeg(params (byte Marker, int Payload)[] segments)
    {
        var ms = new MemoryStream();
        ms.Write([0xFF, 0xD8]);
        foreach (var (marker, payload) in segments)
        {
            ms.Write([0xFF, marker, (byte)((payload + 2) >> 8), (byte)(payload + 2)]);
            ms.Write(new byte[payload]);
        }
        return ms.ToArray();
    }

    private static byte[] WithSof(byte[] prefix, byte marker, int width, int height)
    {
        var ms = new MemoryStream();
        ms.Write(prefix);
        ms.Write([0xFF, marker, 0, 11, 8, (byte)(height >> 8), (byte)height, (byte)(width >> 8), (byte)width, 1, 1, 0x11, 0]);
        return ms.ToArray();
    }

    internal static byte[] JpegWithLargeMetadata(int width, int height, byte sof = 0xC0) =>
        WithSof(
            Jpeg((0xE0, 14), (0xE1, 60_000), (0xE1, 60_000), (0xE1, 60_000), (0xE1, 20_000),
                (0xE2, 65_000), (0xE2, 5_000)),
            sof, width, height);

    [Fact]
    public void Jpeg_dimensions_are_read_past_large_exif_and_icc_segments()
    {
        var jpeg = JpegWithLargeMetadata(4000, 3000);
        jpeg.Length.Should().BeGreaterThan(270_000);
        new ImageDimensionReader().TryRead(new MemoryStream(jpeg), "image/jpeg").Should().Be((4000, 3000));
    }

    [Fact]
    public void Progressive_jpeg_dimensions_are_read()
    {
        var jpeg = JpegWithLargeMetadata(640, 480, sof: 0xC2);
        new ImageDimensionReader().TryRead(new MemoryStream(jpeg), "image/jpeg").Should().Be((640, 480));
    }

    [Fact]
    public void Jpeg_reader_skips_fill_bytes_and_standalone_markers()
    {
        var prefix = Jpeg((0xE0, 14)).Concat(new byte[] { 0xFF, 0xFF, 0xFF, 0xFF, 0x01, 0xFF, 0xD0 }).ToArray();
        var jpeg = WithSof(prefix, 0xC0, 320, 200);
        new ImageDimensionReader().TryRead(new MemoryStream(jpeg), "image/jpeg").Should().Be((320, 200));
    }

    [Fact]
    public void Jpeg_dht_marker_is_not_mistaken_for_a_frame_header()
    {
        var jpeg = WithSof(Jpeg((0xC4, 20), (0xCC, 4)), 0xC1, 50, 40);
        new ImageDimensionReader().TryRead(new MemoryStream(jpeg), "image/jpeg").Should().Be((50, 40));
    }

    [Fact]
    public void Jpeg_truncated_inside_a_segment_is_unknown_and_does_not_throw()
    {
        var jpeg = JpegWithLargeMetadata(4000, 3000);
        var cut = jpeg[..100_000];
        new ImageDimensionReader().TryRead(new MemoryStream(cut), "image/jpeg").Should().BeNull();
    }

    [Fact]
    public void Jpeg_truncated_inside_the_frame_header_is_unknown()
    {
        var jpeg = WithSof(Jpeg((0xE0, 14)), 0xC0, 100, 100);
        new ImageDimensionReader().TryRead(new MemoryStream(jpeg[..^6]), "image/jpeg").Should().BeNull();
    }

    [Fact]
    public void Jpeg_segment_length_beyond_the_end_of_the_stream_is_unknown()
    {
        var jpeg = Jpeg((0xE1, 10));
        jpeg[4] = 0xFF;   // segment length 0xFFxx, far past the end
        new ImageDimensionReader().TryRead(new MemoryStream(jpeg), "image/jpeg").Should().BeNull();
    }

    [Fact]
    public void Jpeg_without_a_frame_header_before_the_scan_is_unknown()
    {
        var jpeg = Jpeg((0xE0, 14), (0xDA, 10));
        new ImageDimensionReader().TryRead(new MemoryStream(jpeg), "image/jpeg").Should().BeNull();
    }

    [Fact]
    public void Jpeg_with_a_zero_dimension_is_unknown()
    {
        var jpeg = WithSof(Jpeg((0xE0, 14)), 0xC0, 0, 100);
        new ImageDimensionReader().TryRead(new MemoryStream(jpeg), "image/jpeg").Should().BeNull();
    }

    [Fact]
    public void Jpeg_with_an_invalid_segment_length_is_unknown()
    {
        byte[] jpeg = [0xFF, 0xD8, 0xFF, 0xE1, 0x00, 0x01, 0, 0, 0, 0, 0, 0, 0, 0];
        new ImageDimensionReader().TryRead(new MemoryStream(jpeg), "image/jpeg").Should().BeNull();
    }

    // APP1 segment: "Exif  ", a TIFF header and an IFD0 holding one Orientation entry.
    internal static byte[] ExifApp1(int orientation, bool bigEndian = false, int entryTag = 0x0112, int entryType = 3)
    {
        var ms = new MemoryStream();
        void U16(int v) { if (bigEndian) ms.Write([(byte)(v >> 8), (byte)v]); else ms.Write([(byte)v, (byte)(v >> 8)]); }
        void U32(int v) { if (bigEndian) { U16(v >> 16); U16(v & 0xFFFF); } else { U16(v & 0xFFFF); U16(v >> 16); } }
        ms.Write("Exif  "u8);
        ms.Write(bigEndian ? "MM"u8 : "II"u8);
        U16(0x2A);
        U32(8);
        U16(1);
        U16(entryTag);
        U16(entryType);
        U32(1);
        U16(orientation);
        U16(0);
        U32(0);
        var payload = ms.ToArray();
        var seg = new byte[payload.Length + 4];
        seg[0] = 0xFF;
        seg[1] = 0xE1;
        seg[2] = (byte)((payload.Length + 2) >> 8);
        seg[3] = (byte)(payload.Length + 2);
        payload.CopyTo(seg, 4);
        return seg;
    }

    private static byte[] JpegWithExif(byte[] app1, int width, int height) =>
        WithSof([.. Jpeg((0xE0, 14)), .. app1, .. Jpeg((0xE2, 30))[2..]], 0xC0, width, height);

    [Theory]
    [InlineData(1, 400, 300)]
    [InlineData(2, 400, 300)]
    [InlineData(3, 400, 300)]
    [InlineData(4, 400, 300)]
    [InlineData(5, 300, 400)]
    [InlineData(6, 300, 400)]
    [InlineData(7, 300, 400)]
    [InlineData(8, 300, 400)]
    public void Jpeg_dimensions_follow_the_exif_orientation(int orientation, int width, int height)
    {
        var jpeg = JpegWithExif(ExifApp1(orientation), 400, 300);
        new ImageDimensionReader().TryRead(new MemoryStream(jpeg), "image/jpeg").Should().Be((width, height));
    }

    [Fact]
    public void Jpeg_exif_orientation_is_read_in_big_endian_byte_order()
    {
        var jpeg = JpegWithExif(ExifApp1(6, bigEndian: true), 400, 300);
        new ImageDimensionReader().TryRead(new MemoryStream(jpeg), "image/jpeg").Should().Be((300, 400));
    }

    [Fact]
    public void Jpeg_without_exif_keeps_its_dimensions()
    {
        var jpeg = WithSof(Jpeg((0xE0, 14)), 0xC0, 400, 300);
        new ImageDimensionReader().TryRead(new MemoryStream(jpeg), "image/jpeg").Should().Be((400, 300));
    }

    [Fact]
    public void Jpeg_exif_orientation_after_large_segments_is_still_applied()
    {
        var jpeg = WithSof([.. Jpeg((0xE0, 14), (0xE2, 65_000)), .. ExifApp1(8), .. Jpeg((0xE2, 60_000))[2..]], 0xC0, 400, 300);
        new ImageDimensionReader().TryRead(new MemoryStream(jpeg), "image/jpeg").Should().Be((300, 400));
    }

    [Fact]
    public void Jpeg_with_an_out_of_range_orientation_keeps_its_dimensions()
    {
        var jpeg = JpegWithExif(ExifApp1(9), 400, 300);
        new ImageDimensionReader().TryRead(new MemoryStream(jpeg), "image/jpeg").Should().Be((400, 300));
    }

    [Fact]
    public void Jpeg_with_a_non_short_orientation_entry_keeps_its_dimensions()
    {
        var jpeg = JpegWithExif(ExifApp1(6, entryType: 4), 400, 300);
        new ImageDimensionReader().TryRead(new MemoryStream(jpeg), "image/jpeg").Should().Be((400, 300));
    }

    [Fact]
    public void Jpeg_with_an_exif_block_without_the_orientation_tag_keeps_its_dimensions()
    {
        var jpeg = JpegWithExif(ExifApp1(6, entryTag: 0x0110), 400, 300);
        new ImageDimensionReader().TryRead(new MemoryStream(jpeg), "image/jpeg").Should().Be((400, 300));
    }

    [Fact]
    public void Jpeg_with_a_truncated_exif_ifd_keeps_its_dimensions()
    {
        // The segment ends inside the IFD entry, but its declared length is consistent with its bytes.
        var full = ExifApp1(6);
        var cutPayload = full[4..^10];
        byte[] seg = [0xFF, 0xE1, (byte)((cutPayload.Length + 2) >> 8), (byte)(cutPayload.Length + 2), .. cutPayload];
        var jpeg = WithSof([.. Jpeg((0xE0, 14)), .. seg], 0xC0, 400, 300);
        new ImageDimensionReader().TryRead(new MemoryStream(jpeg), "image/jpeg").Should().Be((400, 300));
    }

    [Fact]
    public void Jpeg_with_an_ifd_offset_beyond_the_segment_keeps_its_dimensions()
    {
        var seg = ExifApp1(6);
        seg[4 + 6 + 4] = 0xFF;   // IFD0 offset (little-endian) now points far past the segment
        seg[4 + 6 + 5] = 0xFF;
        var jpeg = JpegWithExif(seg, 400, 300);
        new ImageDimensionReader().TryRead(new MemoryStream(jpeg), "image/jpeg").Should().Be((400, 300));
    }

    [Fact]
    public void Jpeg_with_a_bad_tiff_byte_order_mark_keeps_its_dimensions()
    {
        var seg = ExifApp1(6);
        seg[4 + 6] = (byte)'X';
        var jpeg = JpegWithExif(seg, 400, 300);
        new ImageDimensionReader().TryRead(new MemoryStream(jpeg), "image/jpeg").Should().Be((400, 300));
    }

    [Fact]
    public void Reads_a_real_encoder_jpeg()
    {
        var bytes = NetVips.Image.Black(64, 48).Cast(NetVips.Enums.BandFormat.Uchar).WriteToBuffer(".jpg");

        new ImageDimensionReader().TryRead(new MemoryStream(bytes), "image/jpeg").Should().Be((64, 48));
    }

    [Fact]
    public void A_stream_that_fails_while_reading_is_unknown_and_does_not_throw()
    {
        using var failing = new FailingStream(JpegWithLargeMetadata(10, 10), failAfter: 70_000);
        new ImageDimensionReader().TryRead(failing, "image/jpeg").Should().BeNull();
    }

    private sealed class FailingStream(byte[] data, int failAfter) : MemoryStream(data)
    {
        public override int Read(byte[] buffer, int offset, int count) =>
            Position >= failAfter ? throw new IOException("read failed") : base.Read(buffer, offset, count);

        public override int ReadByte() =>
            Position >= failAfter ? throw new IOException("read failed") : base.ReadByte();
    }

    [Fact]
    public void Returns_null_for_non_image()
    {
        var bytes = new MemoryStream(System.Text.Encoding.UTF8.GetBytes("not an image at all"));
        new ImageDimensionReader().TryRead(bytes, "text/plain").Should().BeNull();
    }
}
