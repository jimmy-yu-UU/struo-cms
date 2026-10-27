namespace Struo.Application.Files;

/// <summary>A blob in the byte store: its key and last-modified time (UTC).</summary>
public sealed record StoredBlob(string Key, DateTime LastModifiedUtc);

/// <summary>Pluggable byte storage for file assets. Exactly one implementation is resolved; a later registration replaces the core default.</summary>
public interface IFileStorage
{
    /// <summary>
    /// Persists the bytes under <paramref name="key"/>. <paramref name="contentType"/> is the
    /// already-validated MIME type; backends that can record it (e.g. S3 object metadata) should, so
    /// the type is served correctly on download. Backends that stream through the API (local) ignore it.
    /// </summary>
    Task SaveAsync(string key, Stream content, string contentType, CancellationToken ct = default);

    /// <summary>
    /// Opens the blob for <paramref name="key"/>. Throws
    /// <see cref="Struo.Domain.Query.FileBlobNotFoundException"/> — never a backend-native I/O or
    /// SDK exception — when the key does not exist, so every caller sees the same contract
    /// regardless of which backend is configured.
    /// </summary>
    Task<Stream> OpenReadAsync(string key, CancellationToken ct = default);
    Task DeleteAsync(string key, CancellationToken ct = default);

    /// <summary>
    /// Streams every stored blob (key and last-modified time) without materialising the whole listing:
    /// the local backend enumerates lazily, S3 pages through <c>ListObjectsV2</c>. The order is unspecified.
    /// <see cref="StoredBlob.LastModifiedUtc"/> is UTC; a backend that cannot report a time returns
    /// <see cref="DateTime.MaxValue"/>, which no age cutoff ever reaches.
    /// </summary>
    IAsyncEnumerable<StoredBlob> ListAsync(CancellationToken ct = default);

    /// <summary>True when the backend can mint short-lived direct-download URLs (e.g. S3).</summary>
    bool SupportsPresignedUrls { get; }

    /// <summary>Returns a presigned URL, or null when the backend streams through the API (local).</summary>
    Task<string?> GetPresignedUrlAsync(string key, TimeSpan ttl, CancellationToken ct = default);

    /// <summary>
    /// A one-line description of the storage target for <c>files:sweep</c> to print, so the operator can
    /// check which storage the sweep will compare against the database. It must not contain a credential,
    /// connection string or key: name the bucket, container, endpoint host or root path only. The default
    /// is the implementation's type name.
    /// </summary>
    string Describe() => GetType().Name;
}
