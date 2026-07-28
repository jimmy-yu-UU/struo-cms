namespace Struo.Application.Files;

/// <summary>
/// Requested transform for a stored image. <paramref name="Format"/> is expected to already be
/// an allowlisted value (allowlist enforcement is the caller's responsibility, not this abstraction's).
/// </summary>
/// <param name="Width">Target width in pixels, or null to derive from <paramref name="Height"/>/aspect ratio.</param>
/// <param name="Height">Target height in pixels, or null to derive from <paramref name="Width"/>/aspect ratio.</param>
/// <param name="Format">Target output format (e.g. "webp", "jpeg", "png", "avif"), or null to keep a default.</param>
/// <param name="Fit">Resize fit strategy (e.g. "cover", "contain"/"inside").</param>
/// <param name="Quality">Output quality/compression level, 1-100.</param>
public readonly record struct ImageTransformRequest(
    int? Width, int? Height, string? Format, string Fit, int Quality);

/// <summary>
/// Result of an image transform: the encoded bytes plus the metadata needed to serve them.
/// </summary>
/// <param name="Bytes">Encoded image bytes in the resulting format.</param>
/// <param name="ContentType">MIME content type of <paramref name="Bytes"/> (e.g. "image/webp").</param>
/// <param name="Width">Actual width in pixels of the resulting image.</param>
/// <param name="Height">Actual height in pixels of the resulting image.</param>
public readonly record struct ImageTransformResult(byte[] Bytes, string ContentType, int Width, int Height);

/// <summary>
/// Transforms already-validated, already-stored image bytes (resize / format conversion / quality).
/// Implementations perform no auth or format-allowlist checks — those live at the API boundary.
/// </summary>
public interface IImageTransformer
{
    ImageTransformResult Transform(ReadOnlySpan<byte> source, ImageTransformRequest req);
}
