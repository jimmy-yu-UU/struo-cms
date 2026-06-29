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
