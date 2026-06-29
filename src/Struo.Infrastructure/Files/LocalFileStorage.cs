using Struo.Application.Files;

namespace Struo.Infrastructure.Files;

public sealed class LocalFileStorage(FileStorageOptions options) : IFileStorage
{
    private string Root => options.Local.RootPath;

    private string FullPath(string key)
    {
        // Reject traversal: the resolved path must stay under Root.
        var root = Path.GetFullPath(Root);
        var full = Path.GetFullPath(Path.Combine(root, key));
        if (!full.StartsWith(root, StringComparison.Ordinal))
            throw new InvalidOperationException($"Storage key '{key}' escapes the storage root.");
        return full;
    }

    public async Task SaveAsync(string key, Stream content, CancellationToken ct = default)
    {
        var path = FullPath(key);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await using var fs = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);
        await content.CopyToAsync(fs, ct);
    }

    public Task<Stream> OpenReadAsync(string key, CancellationToken ct = default) =>
        Task.FromResult<Stream>(new FileStream(FullPath(key), FileMode.Open, FileAccess.Read, FileShare.Read));

    public Task DeleteAsync(string key, CancellationToken ct = default)
    {
        var path = FullPath(key);
        if (File.Exists(path)) File.Delete(path);
        return Task.CompletedTask;
    }

    public bool SupportsPresignedUrls => false;
    public Task<string?> GetPresignedUrlAsync(string key, TimeSpan ttl, CancellationToken ct = default) =>
        Task.FromResult<string?>(null);
}
