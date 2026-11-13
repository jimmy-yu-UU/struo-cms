namespace Struo.Application.Files;

/// <summary>
/// Cache for transformed image variant bytes, keyed by a derived key that folds in the source
/// file's identity, its version (so a variant of a replaced version is never served), and the
/// normalized transform parameters. Storage medium is an implementation detail (disk, blob, etc.);
/// this abstraction exposes no IO-specific types. A key always names its file, so all variants of one
/// file can be found and removed without the transform parameters.
/// </summary>
public interface IImageVariantCache
{
    /// <summary>Returns the cached bytes for <paramref name="key"/>, or null on a cache miss.</summary>
    Task<byte[]?> TryGetAsync(string key, CancellationToken ct);

    /// <summary>Stores <paramref name="bytes"/> under <paramref name="key"/>, replacing any prior value,
    /// and drops the file's variants of other versions.</summary>
    Task SetAsync(string key, byte[] bytes, CancellationToken ct);

    /// <summary>
    /// Derives a stable cache key from the source file's identity (<paramref name="fileId"/>),
    /// its current version (<paramref name="fileVersion"/>; a different version derives a different key,
    /// so a variant of another version is never served), and the transform request. Two requests that
    /// differ in any single field (including <paramref name="fileVersion"/>) must derive different keys.
    /// </summary>
    string DeriveKey(Guid fileId, string fileVersion, ImageTransformRequest req);

    /// <summary>
    /// Removes every cached variant of <paramref name="fileId"/>, whatever its version or transform
    /// parameters; a no-op when there are none. Another file's variants are never touched. A cache
    /// shared by several instances is cleared only where this is called.
    /// </summary>
    Task RemoveAllForFileAsync(Guid fileId, CancellationToken ct);
}
