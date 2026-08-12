using Struo.Application.Files;
using Struo.Domain.Query;

namespace Struo.Infrastructure.Files;

public sealed class LocalFileStorage(FileStorageOptions options) : IFileStorage
{
    private string Root => options.Local.RootPath;

    private string FullPath(string key)
    {
        // Reject traversal: the resolved path must stay under Root. Compare against Root plus a
        // trailing separator so a sibling directory sharing the prefix (e.g. "C:\data" vs "C:\dataX")
        // cannot slip through a bare StartsWith.
        var root = Path.GetFullPath(Root);
        var full = Path.GetFullPath(Path.Combine(root, key));
        var rootWithSep = root.EndsWith(Path.DirectorySeparatorChar)
            ? root
            : root + Path.DirectorySeparatorChar;
        if (!full.StartsWith(rootWithSep, StringComparison.Ordinal))
            throw new InvalidOperationException($"Storage key '{key}' escapes the storage root.");
        return full;
    }

    // contentType is ignored: local files are streamed back through the API, which sets the
    // Content-Type from the persisted File row (FilesController.Download), not from the byte store.
    public async Task SaveAsync(string key, Stream content, string contentType, CancellationToken ct = default)
    {
        var path = FullPath(key);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await using var fs = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);
        await content.CopyToAsync(fs, ct);
    }

    public Task<Stream> OpenReadAsync(string key, CancellationToken ct = default)
    {
        try
        {
            return Task.FromResult<Stream>(
                new FileStream(FullPath(key), FileMode.Open, FileAccess.Read, FileShare.Read));
        }
        // A missing blob is an expected condition (DB/storage drift), not a bug: the file's own
        // FileNotFoundException, and DirectoryNotFoundException when an intermediate year/month
        // folder is absent too, both mean the same thing here — translate both to the shared
        // domain exception so the API layer can turn it into a clean 404.
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            throw new FileBlobNotFoundException(key);
        }
    }

    public Task DeleteAsync(string key, CancellationToken ct = default)
    {
        var path = FullPath(key);
        if (System.IO.File.Exists(path)) System.IO.File.Delete(path);
        return Task.CompletedTask;
    }

    public bool SupportsPresignedUrls => false;
    public Task<string?> GetPresignedUrlAsync(string key, TimeSpan ttl, CancellationToken ct = default) =>
        Task.FromResult<string?>(null);
}
