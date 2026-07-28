namespace Struo.Application.Files;

/// <summary>
/// Cache for transformed image variant bytes, keyed by a derived key that folds in the source
/// file's identity, its version (so stale variants are never served after a re-upload), and the
/// normalized transform parameters. Storage medium is an implementation detail (disk, blob, etc.);
/// this abstraction exposes no IO-specific types.
/// </summary>
public interface IImageVariantCache
{
    /// <summary>Returns the cached bytes for <paramref name="key"/>, or null on a cache miss.</summary>
    Task<byte[]?> TryGetAsync(string key, CancellationToken ct);

    /// <summary>Stores <paramref name="bytes"/> under <paramref name="key"/>, replacing any prior value.</summary>
    Task SetAsync(string key, byte[] bytes, CancellationToken ct);

    /// <summary>
    /// Derives a stable cache key from the source file's identity (<paramref name="fileId"/>),
    /// its current version (<paramref name="fileVersion"/> — bump this on re-upload so old variants
    /// naturally miss instead of serving stale bytes), and the transform request. Two requests that
    /// differ in any single field (including <paramref name="fileVersion"/>) must derive different keys.
    /// </summary>
    string DeriveKey(Guid fileId, string fileVersion, ImageTransformRequest req);
}
