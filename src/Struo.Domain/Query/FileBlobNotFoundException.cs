namespace Struo.Domain.Query;

/// <summary>
/// A file's row exists but its byte content is missing from the configured storage backend —
/// deleted directly from disk/bucket, a restored DB dump, or a backend switch that left the row
/// pointing at a key the new backend never received. <c>IFileStorage.OpenReadAsync</c>
/// implementations throw this instead of letting a backend-native I/O exception
/// (<see cref="FileNotFoundException"/>, <see cref="DirectoryNotFoundException"/>, an S3
/// missing-key fault, ...) escape, so the API contract for a missing blob is identical regardless
/// of which backend is configured. Surfaced as HTTP 404 by the API-layer error map.
/// </summary>
public sealed class FileBlobNotFoundException(string key) : Exception($"Storage blob '{key}' was not found.")
{
    public string Key { get; } = key;
}
