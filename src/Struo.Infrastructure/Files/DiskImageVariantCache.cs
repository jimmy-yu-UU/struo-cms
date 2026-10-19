using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
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
/// <remarks>
/// When <c>maxBytes</c> is positive the cache is size-capped: a hit stamps the file's last-write time,
/// and a write that leaves the total above the cap deletes the least recently stamped variants until
/// it is at 90% of the cap. A running byte total, seeded from disk on the first write, decides when a
/// sweep is needed. Eviction failures are logged and never reach the caller.
/// </remarks>
public sealed class DiskImageVariantCache(string rootPath, long maxBytes = 0, ILogger? logger = null)
    : IImageVariantCache
{
    private const int MoveAttempts = 5;
    private const int MoveRetryDelayMs = 10;

    private readonly ILogger _logger = logger ?? NullLogger.Instance;
    private readonly SemaphoreSlim _sweepGate = new(1, 1);
    private readonly object _seedLock = new();
    private long _totalBytes = -1;   // -1 until seeded from disk

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
            // FileShare.Delete lets a concurrent eviction or replacement proceed while this read is open.
            await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete, 4096, useAsync: true);
            var bytes = new byte[stream.Length];
            await stream.ReadExactlyAsync(bytes, ct);
            Touch(path);
            return bytes;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
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
        if (maxBytes > 0 && !TrySeed()) return;
        var replaced = ExistingLength(path);
        var stored = false;
        try
        {
            await System.IO.File.WriteAllBytesAsync(tempPath, bytes, ct);
            stored = await MoveIntoPlaceAsync(tempPath, path, ct);
        }
        catch
        {
            TryDeleteTemp(tempPath);
            throw;
        }

        if (maxBytes <= 0 || !stored) return;
        if (Interlocked.Add(ref _totalBytes, bytes.Length - replaced) > maxBytes) Sweep();
    }

    // The first capped write counts what is already on disk; later writes and evictions adjust the
    // total without touching the directory tree.
    // False when the directory cannot be counted; the variant is then served uncached.
    private bool TrySeed()
    {
        if (Interlocked.Read(ref _totalBytes) >= 0) return true;
        lock (_seedLock)
        {
            if (Interlocked.Read(ref _totalBytes) >= 0) return true;
            try
            {
                Interlocked.Exchange(ref _totalBytes, ListVariants().Sum(v => v.Length));
                return true;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                _logger.LogWarning(ex, "Could not count image variants under {Root}; the variant is served uncached.", rootPath);
                return false;
            }
        }
    }

    private static long ExistingLength(string path)
    {
        try { return new FileInfo(path) is { Exists: true } info ? info.Length : 0; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return 0; }
    }

    private List<FileInfo> ListVariants() =>
        Directory.Exists(rootPath)
            ? new DirectoryInfo(rootPath).EnumerateFiles("*", SearchOption.AllDirectories)
                .Where(v => !v.Name.StartsWith('.')).ToList()
            : [];

    // The destination can be briefly unreplaceable while a concurrent reader, evictor or writer of
    // the same key has it open or pending deletion. A few retries cover that; a variant that still
    // cannot be stored is dropped (the cache is an optimization) and logged.
    private async Task<bool> MoveIntoPlaceAsync(string tempPath, string path, CancellationToken ct)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                System.IO.File.Move(tempPath, path, overwrite: true);
                return true;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                if (attempt < MoveAttempts)
                {
                    await Task.Delay(MoveRetryDelayMs, ct);
                    continue;
                }
                _logger.LogWarning(ex, "Could not store image variant {Path}; it is served uncached.", path);
                TryDeleteTemp(tempPath);
                return false;
            }
        }
    }

    private static void TryDeleteTemp(string tempPath)
    {
        if (!System.IO.File.Exists(tempPath)) return;
        try { System.IO.File.Delete(tempPath); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { /* best-effort cleanup */ }
    }

    private void Touch(string path)
    {
        try { System.IO.File.SetLastWriteTimeUtc(path, DateTime.UtcNow); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogDebug(ex, "Could not stamp image variant {Path}.", path);
        }
    }

    // One sweep at a time; a write that finds a sweep running skips it, and the running sweep or a
    // later write brings the total back down. A sweep re-counts the tree (correcting any drift) and
    // evicts down to 90% of the cap so the next writes have room before another sweep is needed.
    private void Sweep()
    {
        if (!_sweepGate.Wait(0)) return;
        try
        {
            var files = ListVariants();
            var total = files.Sum(v => v.Length);
            Interlocked.Exchange(ref _totalBytes, total);
            if (total <= maxBytes) return;

            var lowWater = maxBytes - maxBytes / 10;
            foreach (var file in files.OrderBy(v => v.LastWriteTimeUtc))
            {
                if (total <= lowWater) break;
                var length = file.Length;
                try
                {
                    file.Delete();
                    total -= length;
                    Interlocked.Add(ref _totalBytes, -length);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    _logger.LogWarning(ex, "Could not evict image variant {Path}.", file.FullName);
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "Image variant cache sweep failed under {Root}.", rootPath);
        }
        finally
        {
            _sweepGate.Release();
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
