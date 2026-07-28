namespace Struo.Application.Files;

/// <summary>
/// Single source of truth for the format→MIME mapping used when serving transformed image bytes.
/// This MUST stay byte-for-byte identical to the mapping baked into
/// <c>Struo.Infrastructure.Files.NetVipsImageTransformer.Transform</c> (its <c>suffix</c>/<c>contentType</c>
/// switch): a cache HIT never re-runs the transformer, so the controller has only <see
/// cref="Struo.Application.Files.ImageTransformRequest.Format"/> to derive the response's Content-Type
/// from, and it must match what a cache MISS would have produced via the transformer.
/// </summary>
public static class ImageContentTypes
{
    public static string ContentTypeFor(string? format) => (format ?? "").ToLowerInvariant() switch
    {
        "webp" => "image/webp",
        "jpeg" or "jpg" => "image/jpeg",
        "png" => "image/png",
        "avif" => "image/avif",
        _ => "image/webp",
    };
}
