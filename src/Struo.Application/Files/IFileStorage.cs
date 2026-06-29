namespace Struo.Application.Files;

/// <summary>Pluggable byte storage for file assets. Exactly one implementation is registered.</summary>
public interface IFileStorage
{
    Task SaveAsync(string key, Stream content, CancellationToken ct = default);
    Task<Stream> OpenReadAsync(string key, CancellationToken ct = default);
    Task DeleteAsync(string key, CancellationToken ct = default);

    /// <summary>True when the backend can mint short-lived direct-download URLs (e.g. S3).</summary>
    bool SupportsPresignedUrls { get; }

    /// <summary>Returns a presigned URL, or null when the backend streams through the API (local).</summary>
    Task<string?> GetPresignedUrlAsync(string key, TimeSpan ttl, CancellationToken ct = default);
}
