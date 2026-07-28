using NetVips;
using Struo.Application.Files;

namespace Struo.Infrastructure.Files;

/// <summary>
/// libvips-backed <see cref="IImageTransformer"/>. Resizes (preserving aspect ratio when only one
/// dimension is given), converts format, and applies output quality. Input bytes are assumed to
/// already be a validated, allowlisted image (no auth/format checks here — see API layer).
/// </summary>
public sealed class NetVipsImageTransformer : IImageTransformer
{
    public ImageTransformResult Transform(ReadOnlySpan<byte> source, ImageTransformRequest req)
    {
        using var img = Image.NewFromBuffer(source);

        var fit = req.Fit switch
        {
            "cover" => Enums.Size.Both,
            "contain" or "inside" => Enums.Size.Down,
            _ => Enums.Size.Down
        };

        using var outImg = (req.Width, req.Height) switch
        {
            (int w, null) => img.ThumbnailImage(w, size: fit),
            (int w, int h) => img.ThumbnailImage(w, height: h, size: fit),
            (null, int h) => img.ThumbnailImage(img.Width, height: h, size: fit),
            _ => img.Copy()
        };

        var (suffix, contentType) = (req.Format ?? "").ToLowerInvariant() switch
        {
            "webp" => (".webp", "image/webp"),
            "jpeg" or "jpg" => (".jpg", "image/jpeg"),
            "png" => (".png", "image/png"),
            "avif" => (".avif", "image/avif"),
            _ => (".webp", "image/webp")
        };

        var bytes = outImg.WriteToBuffer(suffix, new VOption { { "Q", req.Quality } });
        return new ImageTransformResult(bytes, contentType, outImg.Width, outImg.Height);
    }
}
