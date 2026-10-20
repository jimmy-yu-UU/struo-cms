namespace Struo.Application.Files;

public interface IImageDimensionReader
{
    /// <summary>Reads (width, height) from a seekable stream, or null if not a readable image.
    /// A JPEG's result follows its displayed (Exif) orientation: width and height are swapped for
    /// orientations 5-8. Resets the stream position to 0 before returning.</summary>
    (int Width, int Height)? TryRead(Stream seekable, string contentType);
}
