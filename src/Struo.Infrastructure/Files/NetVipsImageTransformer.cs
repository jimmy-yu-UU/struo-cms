using NetVips;
using Struo.Application.Files;

namespace Struo.Infrastructure.Files;

/// <summary>
/// libvips-backed <see cref="IImageTransformer"/>. Resizes (preserving aspect ratio when only one
/// dimension is given), converts format, and applies output quality. Input bytes are assumed to
/// already be a validated, allowlisted image (no auth/format checks here — see API layer).
/// </summary>
/// <remarks>
/// Resizing goes through <c>Image.ThumbnailBuffer</c> (libvips <c>thumbnail_buffer</c>) rather than
/// <c>Image.NewFromBuffer</c> + <c>ThumbnailImage</c>. <c>ThumbnailImage</c> operates on an already
/// fully-decoded image — it does not shrink on load, so a huge source (e.g. 10000x10000) would be
/// decoded at full resolution before being shrunk, regardless of the requested output size.
/// <c>ThumbnailBuffer</c> shrinks on load for codecs that support it (jpeg/webp/etc.), bounding decode
/// memory to roughly the requested output size instead of the source's full resolution. Formats
/// without shrink-on-load support (e.g. png) still decode fully — libvips has no cheaper path for
/// those codecs — but the common large-photo case (jpeg/webp) is bounded.
/// </remarks>
public sealed class NetVipsImageTransformer : IImageTransformer
{
    public ImageTransformResult? Transform(ReadOnlySpan<byte> source, ImageTransformRequest req, long maxSourcePixels)
    {
        var (suffix, contentType) = FormatSuffixAndContentType(req.Format);
        var buffer = source.ToArray(); // NetVips 3.2.0 loads and thumbnails from byte[] only.

        // Opening reads the header only; pixels are decoded on first access. The size checked is the
        // one the decoder produces (for a derived AVIF, the grid or overlay canvas).
        int sourceWidth;
        using (var header = Image.NewFromBuffer(buffer, access: Enums.Access.Sequential))
        {
            if ((long)header.Width * header.Height > maxSourcePixels) return null;
            sourceWidth = header.Width;
        }

        if (req.Width is null && req.Height is null)
        {
            // No resize requested: a pure format conversion at the source's native resolution, so
            // there is no shrink-on-load to gain.
            using var img = Image.NewFromBuffer(buffer);
            using var copy = img.Copy();
            var passthroughBytes = copy.WriteToBuffer(suffix, new VOption { { "Q", req.Quality } });
            return new ImageTransformResult(passthroughBytes, contentType, copy.Width, copy.Height);
        }

        // cover only makes sense — and only genuinely crops — when both dimensions are supplied
        // (crop needs a full target box). Any other fit (inside/contain/unrecognized), or cover
        // with only one dimension given, falls back to "fit within the box, never upscale".
        var cover = req.Fit == "cover" && req.Width is not null && req.Height is not null;
        var size = cover ? Enums.Size.Both : Enums.Size.Down;
        var crop = cover ? Enums.Interesting.Centre : (Enums.Interesting?)null;

        // A height-only request passes the source width as the unconstrained width bound.
        using var outImg = (req.Width, req.Height) switch
        {
            (int w, null) => Image.ThumbnailBuffer(buffer, w, size: size),
            (int w, int h) => Image.ThumbnailBuffer(buffer, w, height: h, size: size, crop: crop),
            (null, int h) => Image.ThumbnailBuffer(buffer, sourceWidth, height: h, size: size),
            _ => throw new InvalidOperationException("unreachable: width/height null case handled above")
        };

        var bytes = outImg.WriteToBuffer(suffix, new VOption { { "Q", req.Quality } });
        return new ImageTransformResult(bytes, contentType, outImg.Width, outImg.Height);
    }

    private static (string Suffix, string ContentType) FormatSuffixAndContentType(string? format) =>
        (format ?? "").ToLowerInvariant() switch
        {
            "webp" => (".webp", "image/webp"),
            "jpeg" or "jpg" => (".jpg", "image/jpeg"),
            "png" => (".png", "image/png"),
            "avif" => (".avif", "image/avif"),
            _ => (".webp", "image/webp")
        };
}
