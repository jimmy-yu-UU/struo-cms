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
/// counted as variants; <see cref="RunMaintenance"/> and every eviction sweep remove the stale ones.
/// </remarks>
public sealed class DiskImageVariantCache(string rootPath, long maxBytes = 0, ILogger? logger = null)
    : IImageVariantCache
{
    private const int MoveAttempts = 5;
    private const int MoveRetryDelayMs = 10;

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
        Directory.CreateDirectory(dir);

        // Atomic write: stage in a uniquely-named temp file in the same directory (so the
        // subsequent move is same-volume and atomic), then move it into place. A reader that
        // stats the final path either sees nothing or the complete file — never a partial one.
        var tempPath = Path.Combine(dir, VariantCacheLayout.TempName(parsed.Variant));
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

        if (!stored) return;
        RemoveOtherVersions(parsed);
        if (maxBytes <= 0) return;
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

    // Only files of the cache layout count: a file in another place, or a temp file, is not a variant.
    private List<FileInfo> ListVariants() =>
        Directory.Exists(rootPath)
            ? new DirectoryInfo(rootPath).EnumerateDirectories()
                .Where(d => VariantCacheLayout.IsFileDirName(d.Name))
                .SelectMany(VariantsOf).ToList()
            : [];

    private static IEnumerable<FileInfo> VariantsOf(DirectoryInfo fileDir) =>
        fileDir.EnumerateDirectories()
            .Where(d => VariantCacheLayout.IsVersionDirName(d.Name))
            .SelectMany(d => d.EnumerateFiles().Where(f => VariantCacheLayout.IsVariantName(f.Name)));

    public Task RemoveAllForFileAsync(Guid fileId, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var dir = Path.Combine(rootPath, VariantCacheLayout.FileDirName(fileId));
        if (!Directory.Exists(dir)) return Task.CompletedTask;
        var freed = SafeLength(() => VariantsOf(new DirectoryInfo(dir)).Sum(v => v.Length));
        try
        {
            Directory.Delete(dir, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ForgetTotal();   // part of the tree may be gone; the next capped write counts the disk again
            throw;
        }
        if (maxBytes > 0 && Interlocked.Read(ref _totalBytes) >= 0) Interlocked.Add(ref _totalBytes, -freed);
        return Task.CompletedTask;
    }

    // Other versions of the file are stale as soon as a newer one is written. Best-effort: a variant
    // that cannot be removed now is removed by a later write, a purge or eviction.
    private void RemoveOtherVersions(VariantCacheLayout.VariantKey written)
    {
        var fileDir = new DirectoryInfo(Path.Combine(rootPath, written.FileDir));
        try
        {
            foreach (var stale in fileDir.EnumerateDirectories()
                         .Where(d => VariantCacheLayout.IsVersionDirName(d.Name) && d.Name != written.Version).ToList())
            {
                var freed = stale.EnumerateFiles().Where(f => VariantCacheLayout.IsVariantName(f.Name)).Sum(f => f.Length);
                stale.Delete(recursive: true);
                if (maxBytes > 0 && Interlocked.Read(ref _totalBytes) >= 0) Interlocked.Add(ref _totalBytes, -freed);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ForgetTotal();
            _logger.LogWarning(ex, "Could not remove the other versions of image variants under {Path}.", fileDir.FullName);
        }
    }

    private void ForgetTotal() => Interlocked.Exchange(ref _totalBytes, -1);

    private static long SafeLength(Func<long> measure)
    {
        try { return measure(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return 0; }
    }

    /// <summary>
    /// One-time upkeep: removes files of the earlier flat layout and temp files left by writes that never
    /// finished. Never throws; failures are logged.
    /// </summary>
    public void RunMaintenance()
    {
        var legacy = LegacyVariantLayout.Remove(rootPath, _logger);
        var temps = RemoveStaleTemps();
        if (legacy + temps > 0)
            _logger.LogInformation(
                "Image variant cache upkeep removed {Legacy} file(s) of the earlier layout and {Temps} stale temp file(s) under {Root}.",
                legacy, temps, rootPath);
    }

    private int RemoveStaleTemps()
    {
        if (!Directory.Exists(rootPath)) return 0;
        var removed = 0;
        var cutoff = DateTime.UtcNow - VariantCacheLayout.StaleTempAge;
        try
        {
            foreach (var temp in new DirectoryInfo(rootPath).EnumerateDirectories()
                         .Where(d => VariantCacheLayout.IsFileDirName(d.Name))
                         .SelectMany(d => d.EnumerateDirectories().Where(v => VariantCacheLayout.IsVersionDirName(v.Name)))
                         .SelectMany(v => v.EnumerateFiles().Where(f => VariantCacheLayout.IsTempName(f.Name)))
                         .Where(f => f.LastWriteTimeUtc < cutoff).ToList())
            {
                if (TryDelete(temp)) removed++;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "Could not scan image cache temp files under {Root}.", rootPath);
        }
        return removed;
    }

    private bool TryDelete(FileInfo file)
    {
        try
        {
            file.Delete();
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "Could not remove image cache file {Path}.", file.FullName);
            return false;
        }
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
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
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

    private string PathFor(string key, out VariantCacheLayout.VariantKey parsed)
    {
        if (!VariantCacheLayout.TryParseKey(key, out parsed))
            throw new ArgumentException("Cache key must be <file id>/<version hex>/<variant hex>.", nameof(key));
        return VariantCacheLayout.VariantPath(rootPath, parsed);
    }
}
