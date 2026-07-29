using System.Text;
using AwesomeAssertions;
using Struo.Infrastructure.Files;
using Xunit;

namespace Struo.Tests.Files;

// Conservative magic-byte sniffing.
public class FileSignatureValidatorTests
{
    private static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00, 0x00, 0x00, 0x0D];
    private static readonly byte[] Jpeg = [0xFF, 0xD8, 0xFF, 0xE0, 0, 0, 0, 0, 0, 0, 0, 0];

    [Fact]
    public void Png_bytes_match_declared_png() =>
        FileSignatureValidator.IsConsistent(Png, "image/png").Should().BeTrue();

    [Fact]
    public void Non_png_bytes_declared_png_are_rejected()
    {
        var html = Encoding.ASCII.GetBytes("<script>alert(1)</script>");
        FileSignatureValidator.IsConsistent(html, "image/png").Should().BeFalse();
    }

    [Fact]
    public void Png_bytes_declared_jpeg_are_rejected() =>
        FileSignatureValidator.IsConsistent(Png, "image/jpeg").Should().BeFalse();

    [Fact]
    public void Jpeg_bytes_match_declared_jpeg() =>
        FileSignatureValidator.IsConsistent(Jpeg, "image/jpeg").Should().BeTrue();

    [Fact]
    public void Unknown_declared_type_is_allowed()
    {
        var anything = Encoding.UTF8.GetBytes("hello world, not an image");
        FileSignatureValidator.IsConsistent(anything, "text/plain").Should().BeTrue();
        FileSignatureValidator.IsConsistent(anything, "application/octet-stream").Should().BeTrue();
    }

    [Fact]
    public void Pdf_signature_is_checked()
    {
        FileSignatureValidator.IsConsistent(Encoding.ASCII.GetBytes("%PDF-1.7"), "application/pdf").Should().BeTrue();
        FileSignatureValidator.IsConsistent(Encoding.ASCII.GetBytes("not a pdf"), "application/pdf").Should().BeFalse();
    }
}
