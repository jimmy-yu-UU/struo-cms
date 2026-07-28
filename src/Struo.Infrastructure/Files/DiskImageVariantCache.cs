using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Struo.Application.Files;

namespace Struo.Infrastructure.Files;

/// <summary>
/// Disk-backed <see cref="IImageVariantCache"/>. Keys are the lowercase-hex SHA256 digest of the
/// source file id, its version, and the normalized transform parameters, so they are inherently
/// free of path separators and traversal sequences — no external input is ever concatenated into
/// a path unsanitized. Variants are sharded into a subdirectory named after the key's first two
/// hex characters (avoids one huge flat directory) and written atomically (temp file + rename) so
/// a concurrent reader never observes a partially-written variant.
/// </summary>
public sealed class DiskImageVariantCache(string rootPath) : IImageVariantCache
{
    public string DeriveKey(Guid fileId, string fileVersion, ImageTransformRequest req)
    {
        // Every field of the request feeds the hash input, each preceded by a distinct tag, so a
        // change to any single field (including fileVersion) changes the hashed bytes.
        var input = string.Create(CultureInfo.InvariantCulture, $"""
            fileId={fileId:N}
            version={fileVersion}
            width={req.Width}
            height={req.Height}
            format={req.Format}
            fit={req.Fit}
            quality={req.Quality}
            """);

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexStringLower(hash);
    }

    public async Task<byte[]?> TryGetAsync(string key, CancellationToken ct)
    {
        var path = PathFor(key);
        if (!System.IO.File.Exists(path)) return null;

        try
        {
            return await System.IO.File.ReadAllBytesAsync(path, ct);
        }
        catch (IOException)
        {
            // Lost a race with a concurrent SetAsync's temp-file rename, or the entry was evicted
            // between the existence check and the read: treat as a miss rather than surfacing an
            // error for what is, functionally, a cache-consistency non-event.
            return null;
        }
    }

    public async Task SetAsync(string key, byte[] bytes, CancellationToken ct)
    {
        var path = PathFor(key);
        var dir = Path.GetDirectoryName(path)!;
        Directory.CreateDirectory(dir);

        // Atomic write: stage in a uniquely-named temp file in the same directory (so the
        // subsequent move is same-volume and atomic), then move it into place. A reader that
        // stats the final path either sees nothing or the complete file — never a partial one.
        var tempPath = Path.Combine(dir, $".{key}.{Guid.NewGuid():N}.tmp");
        try
        {
            await System.IO.File.WriteAllBytesAsync(tempPath, bytes, ct);
            System.IO.File.Move(tempPath, path, overwrite: true);
        }
        catch
        {
            if (System.IO.File.Exists(tempPath))
            {
                try { System.IO.File.Delete(tempPath); } catch (IOException) { /* best-effort cleanup */ }
            }
            throw;
        }
    }

    private string PathFor(string key)
    {
        // Keys are always our own SHA256 hex digest (validated by DeriveKey's contract), so no
        // traversal characters can ever appear here — but guard anyway in case of a caller misuse.
        if (key.Length == 0 || !IsHex(key))
            throw new ArgumentException("Cache key must be non-empty hex.", nameof(key));

        var shard = key[..Math.Min(2, key.Length)];
        return Path.Combine(rootPath, shard, key);
    }

    private static bool IsHex(string s)
    {
        foreach (var c in s)
        {
            var isHexDigit = (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F');
            if (!isHexDigit) return false;
        }
        return true;
    }
}
