using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Struo.Application.Files;

namespace Struo.Infrastructure.Files;

/// <summary>
/// Reads image pixel dimensions from file headers only — it never decodes pixel data, so it
/// adds no dependency and presents almost no attack surface for untrusted uploads. Supports the
/// common web raster formats (PNG, JPEG, GIF, WebP, AVIF); returns null for anything else. Format is
/// detected by magic bytes, not the supplied content type.
/// </summary>
public sealed class ImageDimensionReader(ILogger<ImageDimensionReader>? log = null) : IImageDimensionReader
{
    private readonly ILogger logger = (ILogger?)log ?? NullLogger.Instance;

    public (int Width, int Height)? TryRead(Stream seekable, string contentType)
    {
        try
        {
            if (seekable.CanSeek) seekable.Position = 0;
            var b = ReadHeader(seekable, 64 * 1024);
            return Png(b) ?? Gif(b, seekable) ?? Webp(b) ?? Jpeg(b, seekable) ?? Avif(b, seekable);
        }
        catch (Exception ex) when (ex is IOException or NotSupportedException or InvalidDataException)
        {
            // Truncated or unreadable input: the dimensions are unknown.
            logger.LogDebug(ex, "Image dimensions could not be read");
            return null;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // A parser fault must not turn an upload or a transform probe into a 500.
            logger.LogWarning(ex, "Image dimension reader failed unexpectedly");
            return null;
        }
        finally
        {
            if (seekable.CanSeek) seekable.Position = 0;
        }
    }

    private static byte[] ReadHeader(Stream s, int max)
    {
        using var ms = new MemoryStream();
        var chunk = new byte[8192];
        int read;
        while (ms.Length < max && (read = s.Read(chunk, 0, chunk.Length)) > 0)
            ms.Write(chunk, 0, read);
        return ms.ToArray();
    }

    // PNG: 8-byte signature, then IHDR with width/height as big-endian uint32 at offsets 16/20.
    private static (int, int)? Png(byte[] b)
    {
        if (b.Length < 24) return null;
        if (b[0] != 0x89 || b[1] != 0x50 || b[2] != 0x4E || b[3] != 0x47 ||
            b[4] != 0x0D || b[5] != 0x0A || b[6] != 0x1A || b[7] != 0x0A) return null;
        return (Be32(b, 16), Be32(b, 20));
    }

    // GIF: "GIF87a"/"GIF89a", logical screen width/height as little-endian uint16 at 6/8. libvips
    // sizes the canvas to fit every frame, so the result is the largest of the logical screen and each
    // image descriptor's offset plus size. Blocks are skipped, never decoded.
    private static (int, int)? Gif(byte[] b, Stream s)
    {
        if (b.Length < 10) return null;
        if (b[0] != (byte)'G' || b[1] != (byte)'I' || b[2] != (byte)'F') return null;
        int w = b[6] | (b[7] << 8), h = b[8] | (b[9] << 8);
        if (b.Length < 13) return (w, h);

        s.Position = 13;
        if ((b[10] & 0x80) != 0 && !Skip(s, 3L << ((b[10] & 7) + 1))) return (w, h);
        var desc = new byte[9];
        while (true)
        {
            var block = s.ReadByte();
            if (block == 0x21)
            {
                if (s.ReadByte() < 0 || !SkipSubBlocks(s)) break;
            }
            else if (block == 0x2C)
            {
                if (s.Read(desc, 0, 9) < 9) break;
                w = Math.Max(w, (desc[0] | (desc[1] << 8)) + (desc[4] | (desc[5] << 8)));
                h = Math.Max(h, (desc[2] | (desc[3] << 8)) + (desc[6] | (desc[7] << 8)));
                if ((desc[8] & 0x80) != 0 && !Skip(s, 3L << ((desc[8] & 7) + 1))) break;
                if (s.ReadByte() < 0 || !SkipSubBlocks(s)) break;   // LZW minimum code size, then data
            }
            else break;   // trailer, end of stream or an unknown block
        }
        return (w, h);
    }

    private static bool Skip(Stream s, long count)
    {
        s.Seek(count, SeekOrigin.Current);
        return s.Position <= s.Length;
    }

    private static bool SkipSubBlocks(Stream s)
    {
        while (true)
        {
            var size = s.ReadByte();
            if (size < 0) return false;
            if (size == 0) return true;
            if (!Skip(s, size)) return false;
        }
    }

    // WebP: "RIFF"<size>"WEBP"<fourcc>... — handles lossy (VP8 ), lossless (VP8L), extended (VP8X).
    private static (int, int)? Webp(byte[] b)
    {
        if (b.Length < 30) return null;
        if (b[0] != (byte)'R' || b[1] != (byte)'I' || b[2] != (byte)'F' || b[3] != (byte)'F' ||
            b[8] != (byte)'W' || b[9] != (byte)'E' || b[10] != (byte)'B' || b[11] != (byte)'P') return null;

        var fourcc = System.Text.Encoding.ASCII.GetString(b, 12, 4);
        switch (fourcc)
        {
            case "VP8 ": // lossy: 14-bit width/height (little-endian) at offset 26/28
                return ((b[26] | (b[27] << 8)) & 0x3FFF, (b[28] | (b[29] << 8)) & 0x3FFF);
            case "VP8L": // lossless: signature 0x2F at 20, then 14+14 bits from offset 21
                if (b[20] != 0x2F) return null;
                int bits = b[21] | (b[22] << 8) | (b[23] << 16) | (b[24] << 24);
                return ((bits & 0x3FFF) + 1, ((bits >> 14) & 0x3FFF) + 1);
            case "VP8X": // extended: 24-bit (width-1)/(height-1) little-endian at 24/27
                return ((b[24] | (b[25] << 8) | (b[26] << 16)) + 1,
                        (b[27] | (b[28] << 8) | (b[29] << 16)) + 1);
            default:
                return null;
        }
    }

    // JPEG: FF D8, then marker segments walked by their length field up to the first SOFn, which
    // carries height/width as big-endian uint16. Segments are skipped, never decoded, and the walk ends
    // at SOS/EOI or the end of the stream. Reads only, so a stream that cannot report its length works.
    private static (int, int)? Jpeg(byte[] b, Stream s)
    {
        if (b.Length < 4 || b[0] != 0xFF || b[1] != 0xD8) return null;
        s.Position = 2;
        var frame = new byte[5];
        while (true)
        {
            var c = s.ReadByte();
            if (c < 0) return null;
            if (c != 0xFF) continue;

            int marker;
            do marker = s.ReadByte(); while (marker == 0xFF);   // 0xFF fill bytes may precede a marker
            if (marker < 0) return null;
            if (marker == 0x00 || marker == 0x01 || (marker >= 0xD0 && marker <= 0xD8)) continue;   // stuffed byte, TEM, RSTn, SOI
            if (marker == 0xD9 || marker == 0xDA) return null;   // EOI, SOS: no frame header before the scan

            var hi = s.ReadByte();
            var lo = s.ReadByte();
            if (lo < 0) return null;
            var len = (hi << 8) | lo;
            if (len < 2) return null;

            // SOF0-15 carry dimensions, EXCEPT DHT(C4), JPG(C8), DAC(CC).
            var isSof = marker >= 0xC0 && marker <= 0xCF && marker != 0xC4 && marker != 0xC8 && marker != 0xCC;
            if (!isSof)
            {
                if (!SkipByReading(s, len - 2)) return null;
                continue;
            }
            if (len < 7 || s.Read(frame, 0, 5) < 5) return null;
            int h = (frame[1] << 8) | frame[2], w = (frame[3] << 8) | frame[4];
            return w > 0 && h > 0 ? (w, h) : null;
        }
    }

    private static bool SkipByReading(Stream s, int count)
    {
        var scratch = new byte[Math.Min(count, 8192)];
        while (count > 0)
        {
            var read = s.Read(scratch, 0, Math.Min(count, scratch.Length));
            if (read <= 0) return false;
            count -= read;
        }
        return true;
    }

    // AVIF (ISO BMFF): an "ftyp" box with an avif brand, then "ispe" property boxes whose payload is
    // 4 bytes version/flags followed by width and height as big-endian uint32.
    private static (int, int)? Avif(byte[] head, Stream s)
    {
        var b = head;
        if (b.Length < 16 || b[4] != (byte)'f' || b[5] != (byte)'t' || b[6] != (byte)'y' || b[7] != (byte)'p')
            return null;
        var brand = System.Text.Encoding.ASCII.GetString(b, 8, 4);
        if (brand is not ("avif" or "avis")) return null;

        // Property boxes can sit anywhere in the file, so the whole stream is scanned; a decoy ispe
        // early must not hide the real one.
        s.Position = 0;
        b = ReadHeader(s, int.MaxValue);
        if (HasDerivedImage(b)) return null;
        (int, int)? largest = null;
        long largestArea = -1;
        for (var i = 8; i + 16 <= b.Length; i++)
        {
            if (b[i] != (byte)'i' || b[i + 1] != (byte)'s' || b[i + 2] != (byte)'p' || b[i + 3] != (byte)'e')
                continue;
            var w = (int)Math.Min(BeU32(b, i + 8), int.MaxValue);
            var h = (int)Math.Min(BeU32(b, i + 12), int.MaxValue);
            if ((long)w * h <= largestArea) continue;
            largestArea = (long)w * h;
            largest = (w, h);
        }
        return largest;
    }

    // A derived image (grid or overlay) is sized by item data, not by any ispe, so the mere presence
    // of its item type or the "dimg" reference makes the file not safely sizable. A chance match in
    // compressed data only means the original is served.
    private static bool HasDerivedImage(byte[] b) =>
        b.AsSpan().IndexOf("grid"u8) >= 0 || b.AsSpan().IndexOf("iovl"u8) >= 0 || b.AsSpan().IndexOf("dimg"u8) >= 0;

    private static long BeU32(byte[] b, int o) =>
        ((long)b[o] << 24) | ((long)b[o + 1] << 16) | ((long)b[o + 2] << 8) | b[o + 3];

    private static int Be32(byte[] b, int o) =>
        (b[o] << 24) | (b[o + 1] << 16) | (b[o + 2] << 8) | b[o + 3];
}
