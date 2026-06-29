namespace Struo.Application.Files;

public interface IImageDimensionReader
{
    /// <summary>Reads (width, height) from a seekable stream, or null if not a readable image.
    /// Resets the stream position to 0 before returning.</summary>
    (int Width, int Height)? TryRead(Stream seekable, string contentType);
}
