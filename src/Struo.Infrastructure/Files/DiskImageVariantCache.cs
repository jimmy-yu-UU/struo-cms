using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Struo.Application.Files;

namespace Struo.Infrastructure.Files;

/// <summary>
/// Disk-backed <see cref="IImageVariantCache"/>. A variant lives at
/// <c>&lt;root&gt;/&lt;file id&gt;/&lt;version&gt;/&lt;variant&gt;</c> (<see cref="VariantCacheLayout"/>), so every
/// variant of one file sits under one directory and can be removed without knowing the transform
/// parameters. A key is the three components joined by <c>/</c>; it is parsed back and each component
/// validated before any path is built, so no caller text can leave the root. Variants are written
/// atomically (temp file + rename) so a concurrent reader never observes a partial one.
/// </summary>
/// <remarks>
/// Writing a variant of one version removes the file's variants of other versions. When
/// <c>maxBytes</c> is positive the cache is size-capped: a hit stamps the file's last-write time,
/// and a write that leaves the total above the cap deletes the least recently stamped variants until
/// it is at 90% of the cap. A running byte total, seeded from disk on the first write, decides when a
/// sweep is needed. Eviction failures are logged and never reach the caller. Temp files are never
/// counted as variants. The stale ones (older than <see cref="VariantCacheLayout.StaleTempAge"/>) are
/// removed by <see cref="RunMaintenance"/> at start, by every eviction sweep, and, for the file written,
/// by each write; the sweep and the start also remove empty directories of the layout. A zero or negative
/// <c>maxBytes</c> means no cap. The running total can read low (two removals of the same files overlap)
/// or high (two writers of one key both add its length; a purge found the directory already gone). The
/// next sweep, a failed removal (it resets the total so the next write counts the disk again) and a
/// restart bring it back in line. Symbolic links and junctions are never followed or removed.
/// </remarks>
public sealed partial class DiskImageVariantCache(string rootPath, long maxBytes = 0, ILogger? logger = null)
    : IImageVariantCache
{
    private const int MoveAttempts = 5;
    private const int MoveRetryDelayMs = 10;
    private const int PurgeRetryDelayMs = 10;
    internal const int StoreAttempts = 5;
    internal const int PurgeAttempts = 3;

    private readonly ILogger _logger = logger ?? NullLogger.Instance;
    private readonly SemaphoreSlim _sweepGate = new(1, 1);
    private readonly Lock _seedLock = new();
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

        var variant = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(input)));
        var version = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(fileVersion)))
            [..VariantCacheLayout.VersionLength];
        return VariantCacheLayout.ComposeKey(fileId, version, variant);
    }

    public async Task<byte[]?> TryGetAsync(string key, CancellationToken ct)
    {
        var path = PathFor(key, out _);
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
        var path = PathFor(key, out var parsed);
        var dir = Path.GetDirectoryName(path)!;

        // Atomic write: stage in a uniquely-named temp file in the same directory (so the
        // subsequent move is same-volume and atomic), then move it into place. A reader that
        // stats the final path either sees nothing or the complete file — never a partial one.
        var tempPath = Path.Combine(dir, VariantCacheLayout.TempName(parsed.Variant));
        if (maxBytes > 0 && !TrySeed()) return;
        long? replaced;
        try
        {
            replaced = await StoreAsync(dir, tempPath, path, bytes, ct);
        }
        catch
        {
            TryDeleteTemp(tempPath);
            throw;
        }

        if (replaced is null) return;
        VariantLanded?.Invoke(dir);
        TidyFileDirectory(parsed);
        if (maxBytes > 0 && AdjustTotal(bytes.Length - replaced.Value) > maxBytes) Sweep();
    }

    // Creates the directory, writes the temp file and moves it into place; returns the length of the file
    // the move replaced, or null when the variant was not stored. The directory can legitimately vanish at
    // any step (a purge of the file, another version's write, a maintenance pass), so a missing path
    // starts the attempt again. The replaced length is measured inside the attempt, because whatever
    // removed the directory already took the old file off the total. From the second attempt on, a file
    // directory that is gone means the file was purged (the one other remover, a sweep of an hour-old
    // empty directory, gives the same answer), so nothing is recreated and the write is a miss. When the
    // attempts run out the variant is dropped (the cache is an optimization) and logged once. Both
    // "directory not found" and "file not found" mean the same here, whatever the platform throws.
    private async Task<long?> StoreAsync(string dir, string tempPath, string path, byte[] bytes, CancellationToken ct)
    {
        Exception? last = null;
        for (var attempt = 1; attempt <= StoreAttempts; attempt++)
        {
            if (attempt > 1 && !Directory.Exists(Path.GetDirectoryName(dir)!)) return null;
            try
            {
                Directory.CreateDirectory(dir);
                DirectoryEnsured?.Invoke(dir);
                await System.IO.File.WriteAllBytesAsync(tempPath, bytes, ct);
                var replaced = ExistingLength(path);
                return await MoveIntoPlaceAsync(tempPath, path, ct) ? replaced : null;
            }
            catch (Exception ex) when (VariantCacheLayout.IsMissingPath(ex))
            {
                last = ex;
                TryDeleteTemp(tempPath);
            }
        }

        if (_logger.IsEnabled(LogLevel.Warning))
            _logger.LogWarning(
                last, "Could not store image variant {Path}: its directory kept disappearing; it is served uncached.", path);
        return null;
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

    // Only files of the cache layout count: a file in another place, or a temp file, is not a variant.
    private List<FileInfo> ListVariants() =>
        Directory.Exists(rootPath)
            ? new DirectoryInfo(rootPath).EnumerateDirectories()
                .Where(d => VariantCacheLayout.IsFileDirName(d.Name) && !VariantCacheLayout.IsReparsePoint(d))
                .SelectMany(VariantsOf).ToList()
            : [];

    private static IEnumerable<FileInfo> VariantsOf(DirectoryInfo fileDir) =>
        fileDir.EnumerateDirectories()
            .Where(d => VariantCacheLayout.IsVersionDirName(d.Name) && !VariantCacheLayout.IsReparsePoint(d))
            .SelectMany(VariantsIn);

    public async Task RemoveAllForFileAsync(Guid fileId, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var dir = Path.Combine(rootPath, VariantCacheLayout.FileDirName(fileId));
        if (!Directory.Exists(dir) || VariantCacheLayout.IsReparsePoint(new DirectoryInfo(dir)))
            return;
        var freed = SafeLength(() => VariantsOf(new DirectoryInfo(dir)).Sum(v => v.Length));
        IOException? remaining = null;
        var retried = false;
        for (var attempt = 1; attempt <= PurgeAttempts; attempt++)
        {
            if (attempt > 1) await Task.Delay(PurgeRetryDelayMs, ct);   // a writer or a pending delete finishes
            try
            {
                DeleteTree(dir);
                remaining = null;
                break;
            }
            catch (Exception ex) when (VariantCacheLayout.IsMissingPath(ex) && !Directory.Exists(dir))
            {
                return;   // the directory itself is gone: another purge removed it first, and accounted for it
            }
            catch (IOException ex) when (
                VariantCacheLayout.IsMissingPath(ex) || VariantCacheLayout.IsDirectoryNotEmpty(ex))
            {
                remaining = ex;   // a sub-entry vanished, or a writer added one, while the tree was removed
                retried = true;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                ForgetTotal();   // part of the tree may be gone; the next capped write counts the disk again
                throw;
            }
        }

        FinishPurgeAccounting(dir, freed, retried, remaining);
    }

    // Bytes a writer added while the purge ran are unknown, so any retry resets the total for a recount;
    // entries that remain after the last attempt are a warning (a purge must not leave variants behind).
    private void FinishPurgeAccounting(string dir, long freed, bool retried, IOException? remainingFailure)
    {
        if (remainingFailure is not null)
        {
            ForgetTotal();
            if (_logger.IsEnabled(LogLevel.Warning))
                _logger.LogWarning(remainingFailure, "Image variants of a purged file remain under {Path}.", dir);
            return;
        }

        if (maxBytes <= 0) return;
        if (retried) ForgetTotal();
        else AdjustTotal(-freed);
    }

    /// <summary>Test seam: called with the variant's directory once it exists and before the temp file is written.</summary>
    internal Action<string>? DirectoryEnsured { get; set; }

    /// <summary>Test seam: called with the variant's directory once the variant has landed, before the tidy.</summary>
    internal Action<string>? VariantLanded { get; set; }

    /// <summary>Test seam: removes a file's directory tree for a purge.</summary>
    internal Action<string> DeleteTree { get; set; } = static dir => Directory.Delete(dir, recursive: true);

    internal long TrackedBytes => Interlocked.Read(ref _totalBytes);

    /// <summary>The total after adding <paramref name="delta"/>: a total that is not counted (negative) stays
    /// not counted, and a counted one never drops below zero.</summary>
    internal static long ApplyDelta(long current, long delta) =>
        current < 0 ? current : Math.Max(0, current + delta);

    // The only way the running total changes after it is counted. Returns the new total, or -1 while it
    // is not counted, so a failed removal that reset it is never turned into a small positive number.
    private long AdjustTotal(long delta)
    {
        while (true)
        {
            var current = Interlocked.Read(ref _totalBytes);
            var next = ApplyDelta(current, delta);
            if (next == current || Interlocked.CompareExchange(ref _totalBytes, next, current) == current)
                return next;
        }
    }

    private void ForgetTotal() => Interlocked.Exchange(ref _totalBytes, -1);

    private static long SafeLength(Func<long> measure)
    {
        try { return measure(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return 0; }
    }

    // The destination can be briefly unreplaceable while a concurrent reader, evictor or writer of
    // the same key has it open or pending deletion. A few retries cover that; a variant that still
    // cannot be stored is dropped (the cache is an optimization) and logged.
    private async Task<bool> MoveIntoPlaceAsync(string tempPath, string path, CancellationToken ct)
    {
        Exception? last = null;
        for (var attempt = 1; attempt <= MoveAttempts; attempt++)
        {
            try
            {
                System.IO.File.Move(tempPath, path, overwrite: true);
                return true;
            }
            catch (Exception ex) when ((ex is IOException or UnauthorizedAccessException)
                && !VariantCacheLayout.IsMissingPath(ex))
            {
                last = ex;
                if (attempt < MoveAttempts) await Task.Delay(MoveRetryDelayMs, ct);
            }
        }
        _logger.LogWarning(last, "Could not store image variant {Path}; it is served uncached.", path);
        TryDeleteTemp(tempPath);
        return false;
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
            if (_logger.IsEnabled(LogLevel.Debug))
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
            RemoveStaleTemps();
            EvictToLowWater();
            RemoveEmptyDirectories();
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

    private void EvictToLowWater()
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
                AdjustTotal(-length);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                _logger.LogWarning(ex, "Could not evict image variant {Path}.", file.FullName);
            }
        }
    }

    private string PathFor(string key, out VariantCacheLayout.VariantKey parsed)
    {
        if (!VariantCacheLayout.TryParseKey(key, out parsed))
            throw new ArgumentException(
                "Cache key must be <file id>/<version hex>/<variant hex>.", nameof(key));
        return VariantCacheLayout.VariantPath(rootPath, parsed);
    }
}
